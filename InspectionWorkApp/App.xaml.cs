using InspectionWorkApp.Controllers;
using InspectionWorkApp.Interfaces;
using InspectionWorkApp.Jobs;
using InspectionWorkApp.Models;
using InspectionWorkApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Extensions.Logging.File;
using Quartz;
using Quartz.Impl;
using QuartzLog = Quartz.Logging;
using Quartz.Spi;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Windows;

namespace InspectionWorkApp
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        public static IServiceProvider ServiceProvider { get; private set; }
        //private readonly IDbContextFactory<YourDbContext> _dbContextFactory;
        //private readonly ILogger<App> _logger;

        public App()
        {
            var services = new ServiceCollection();
            // Получаем путь к AppData\Roaming\InspectionWorkApp (независимо от версии)
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InspectionWorkApp"  // Имя вашей подпапки — можно изменить
            );

            // Создаём подпапку, если её нет (один раз)
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            string localConfigPath = Path.Combine(appDataPath, "localsettings.json");
            // Настройка конфигурации
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile(localConfigPath, optional: true, reloadOnChange: true)  // ← localsettings.json из AppData
                .Build();

            // Логируем для отладки
            var tempLogger = new LoggerFactory().CreateLogger<App>();  // Временный logger
            tempLogger?.LogInformation("Config loaded: AppData path={AppDataPath}", appDataPath);
            tempLogger?.LogInformation("Local config exists: {Exists}", File.Exists(localConfigPath));

            // Регистрация строки подключения
            services.AddSingleton(configuration);

            // Регистрация DbContext
            services.AddDbContextFactory<YourDbContext>(options =>
    options.UseSqlServer(
        configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5, // Максимальное количество попыток
            maxRetryDelay: TimeSpan.FromSeconds(10), // Максимальная задержка между попытками
            errorNumbersToAdd: null // Дополнительные коды ошибок SQL Server, если нужно
        )
    )
);
            // После AddSingleton(configuration)
            services.Configure<DatabaseSettings>(configuration.GetSection("DatabaseSettings"));
            services.AddSingleton(provider => provider.GetRequiredService<IOptions<DatabaseSettings>>().Value);
            // Регистрация логирования
            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));

                builder.AddConsole();
                builder.AddDebug();

                // ────────────────────────────────────────────────────────────────
                // Самый простой и рабочий вариант для версии 3.0.0+
                builder.AddFile(
                    pathFormat: "logs/db-connection-{Date}.log",   // {Date} → ежедневная ротация
                    minimumLevel: LogLevel.Information,
                    fileSizeLimitBytes: 10 * 1024 * 1024,          // 10 МБ
                    retainedFileCountLimit: 31,                    // храним 31 день
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                );
                // ────────────────────────────────────────────────────────────────
            });
            // В конструкторе App
            services.AddSingleton(provider =>
            {
                var factory = provider.GetRequiredService<IDbContextFactory<YourDbContext>>();
                var logger = provider.GetRequiredService<ILogger<App>>();
                var machineName = Environment.MachineName;

                string? comPort = null; // default
                try
                {
                    using var context = factory.CreateDbContext();
                    comPort = context.dic_PCNameSector
                        .Where(pc => pc.NamePC == machineName)
                        .Select(pc => pc.CardReaderCOMPort)
                        .FirstOrDefault();
                    logger.LogInformation("Loaded COMPort '{COMPort}' for machine '{MachineName}' from DB",
                        comPort, machineName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load COMPort from DB");
                }

                return new COMControllerParamsModel
                {
                    PortName = comPort,
                    BaudRate = 9600,
                    Parity = System.IO.Ports.Parity.None,
                    DataBits = 8,
                    StopBits = System.IO.Ports.StopBits.One
                };
            });

            // Регистрация сервисов
            services.AddSingleton<COMController>();
            services.AddSingleton<Controller1C>();
            services.AddSingleton<DataAccessLayer>();
            services.AddSingleton<IEmployeeRepository>(sp => sp.GetRequiredService<DataAccessLayer>());
            services.AddSingleton<OperatorService>(sp => new OperatorService(
                sp.GetRequiredService<IEmployeeRepository>(),
                sp.GetRequiredService<Controller1C>(),
                sp.GetRequiredService<ILogger<OperatorService>>(),
                sp.GetRequiredService<COMController>()));
            services.AddScoped<GenerateAssignmentsJob>();
            services.AddScoped<MainWindow>(provider =>
            {
                var mainWindow = ActivatorUtilities.CreateInstance<MainWindow>(provider);
                return mainWindow;
            });
            services.AddScoped<AdminWindow>();
            services.AddSingleton<DataInitializer>();
            services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();
            services.AddSingleton(provider =>
            {
                var factory = provider.GetRequiredService<ISchedulerFactory>();
                var scheduler = factory.GetScheduler().GetAwaiter().GetResult();
                scheduler.JobFactory = new SimpleJobFactory(provider);
                return scheduler;
            });

            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InspectionWorkApp"
        );
                string localPath = Path.Combine(appDataPath, "localsettings.json");

                if (!File.Exists(localPath))
                {
                    // Создаём шаблон
                    var template = @"{
""ConnectionStrings"": {
""DefaultConnection"": ""<ВСТАВЬТЕ СТРОКУ ПОДКЛЮЧЕНИЯ>""
    },
""DatabaseSettings"": {
""TargetDatabase"": ""<ИМЯ БД>""
    }
}";
                    File.WriteAllText(localPath, template);

                    var loggerr = _serviceProvider.GetRequiredService<ILogger<App>>();
                    loggerr.LogWarning("Создан шаблон localsettings.json в {Path}. Отредактируйте его и перезапустите приложение.", localPath);

                    MessageBox.Show($"Создан шаблон конфигурации в:\n{localPath}\n\nОтредактируйте localsettings.json и перезапустите приложение.");
                    Shutdown();
                    return;
                }
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                var configuration = _serviceProvider.GetRequiredService<IConfiguration>();
                logger.LogInformation("Starting application...");
                var dbSettings = _serviceProvider.GetRequiredService<DatabaseSettings>();
                // **ЧИТАЕМ ПАРАМЕТРЫ ИЗ БД**
                (int? roleId, string cardNumber, Guid? sessionId) = await ReadStartupParametersFromDB(logger, configuration, dbSettings);

                // Инициализация начальных данных
                var initializer = _serviceProvider.GetRequiredService<DataInitializer>();
                await initializer.InitializeAsync();
                logger.LogInformation("Initial data loaded.");

                // Настройка Quartz
                var scheduler = _serviceProvider.GetRequiredService<IScheduler>();
                await scheduler.Start();
                logger.LogInformation("Quartz scheduler started.");
                var assignmentJob = JobBuilder.Create<GenerateAssignmentsJob>()
                    .WithIdentity("GenerateAssignmentsJob", "default")
                    .StoreDurably() // Хранить задачу без триггера
                    .Build();

                await scheduler.AddJob(assignmentJob, false);
                logger.LogInformation("GenerateAssignmentsJob registered for manual triggering.");

                var mainWindow = _serviceProvider.GetService<MainWindow>();
                if (mainWindow != null && roleId.HasValue && !string.IsNullOrEmpty(cardNumber))
                {
                    mainWindow.SetStartupParameters(roleId.Value, cardNumber);
                    logger.LogInformation("✓ AUTO-INIT from DB: RoleId={RoleId}, CardNumber={CardNumber}, Session={SessionId}",
                        roleId, cardNumber, sessionId);
                }
                else
                {
                    logger.LogInformation("⚠ MANUAL MODE: Waiting for card authentication");
                }
                if (mainWindow == null)
                {
                    logger.LogError("Failed to resolve MainWindow: service not registered.");
                    MessageBox.Show("Не удалось создать MainWindow: сервис не зарегистрирован.");
                    Shutdown();
                    return;
                }

                mainWindow.Show();
                logger.LogInformation("MainWindow displayed successfully.");
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider?.GetService<ILogger<App>>();
                logger?.LogError(ex, "Application startup failed.");
                MessageBox.Show($"Ошибка при запуске приложения: {ex.Message}");
                Shutdown();
                throw;
            }
        }
        private static async Task<(int? RoleId, string CardNumber, Guid? SessionId)> ReadStartupParametersFromDB(ILogger logger, IConfiguration configuration, DatabaseSettings dbSettings)
        {
            int? roleId = null;
            string cardNumber = null;
            Guid? sessionId = null;

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                logger.LogError("Connection string 'DefaultConnection' not found in appsettings.json");
                return (null, null, null);
            }
            // ← Формируем имя таблицы динамически
            string exchangeTable = $"{dbSettings.TargetDatabase}.dbo.InspectionWorkAppExchange";
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // 1. ОЧИСТКА старых записей
                using var cleanupCmd = new SqlCommand($@"
            DELETE FROM {exchangeTable} 
            WHERE CreatedAt < DATEADD(HOUR, -1, GETDATE())", connection);
                int cleaned = await cleanupCmd.ExecuteNonQueryAsync();
                if (cleaned > 0)
                    logger.LogInformation("Cleaned {Count} old exchange records", cleaned);

                // 2. ЧИТАЕМ параметры
                using var selectCmd = new SqlCommand($@"
            SELECT TOP 1 RoleId, CardNumber, LaunchSession 
            FROM {exchangeTable} 
            WHERE Processed = 0 
            ORDER BY CreatedAt DESC", connection);

                using var reader = await selectCmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    roleId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
                    cardNumber = reader.IsDBNull(1) ? null : reader.GetString(1);
                    sessionId = reader.GetGuid(2);

                    reader.Close();  // ← КРИТИЧНО: Закрываем Reader!
                }
                else
                {
                    reader.Close();  // ← Закрываем даже если ничего не нашли
                    return (null, null, null);
                }

                // 3. UPDATE на том же соединении (теперь безопасно!)
                using var updateCmd = new SqlCommand($@"
            UPDATE {exchangeTable}
            SET Processed = 1, ProcessedAt = GETDATE() 
            WHERE LaunchSession = @sessionId", connection);
                updateCmd.Parameters.AddWithValue("@sessionId", sessionId.Value);
                int updated = await updateCmd.ExecuteNonQueryAsync();

                if (updated > 0)
                {
                    logger.LogInformation("Parameters from DB processed: Session={SessionId}", sessionId);
                    return (roleId, cardNumber, sessionId);
                }
                else
                {
                    logger.LogWarning("Failed to mark parameters as processed: Session={SessionId}", sessionId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read startup parameters from DB");
            }

            return (null, null, null);
        }

        public class SimpleJobFactory : IJobFactory
        {
            private readonly IServiceProvider _provider;

            public SimpleJobFactory(IServiceProvider provider)
            {
                _provider = provider;
            }

            public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
            {
                return _provider.GetService(bundle.JobDetail.JobType) as IJob
                       ?? throw new InvalidOperationException($"Failed to resolve job type {bundle.JobDetail.JobType.Name}");
            }

            public void ReturnJob(IJob job) { }
        }
    }
}