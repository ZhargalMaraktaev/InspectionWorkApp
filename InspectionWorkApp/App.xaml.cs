using InspectionWorkApp.Controllers;
using InspectionWorkApp.Interfaces;
using InspectionWorkApp.Jobs;
using InspectionWorkApp.Models;
using InspectionWorkApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using System;
using System.IO;
using System.Windows;

namespace InspectionWorkApp
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        public static IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            var services = new ServiceCollection();

            // Настройка конфигурации
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Регистрация строки подключения
            services.AddSingleton(configuration);

            // Регистрация DbContext
            services.AddDbContextFactory<YourDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            // Регистрация логирования
            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddConsole();
                builder.AddDebug();
            });

            // Регистрация параметров COM-порта
            services.AddSingleton(new COMControllerParamsModel
            {
                PortName = "COM3",
                BaudRate = 9600,
                Parity = System.IO.Ports.Parity.None,
                DataBits = 8,
                StopBits = System.IO.Ports.StopBits.One
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
            services.AddScoped<GenerateTasksJob>();
            services.AddScoped<GenerateAssignmentsJob>();
            services.AddScoped<MainWindow>();
            services.AddScoped<AdminWindow>();
            services.AddSingleton<DataInitializer>();

            // Регистрация Quartz ISchedulerFactory
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
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Starting application...");

                // Инициализация начальных данных
                var initializer = _serviceProvider.GetRequiredService<DataInitializer>();
                await initializer.InitializeAsync();
                logger.LogInformation("Initial data loaded.");

                // Настройка Quartz
                var scheduler = _serviceProvider.GetRequiredService<IScheduler>();
                await scheduler.Start();
                logger.LogInformation("Quartz scheduler started.");

                // Задача для создания задач
                var taskJob = JobBuilder.Create<GenerateTasksJob>()
                    .WithIdentity("GenerateTasks", "Default")
                    .Build();

                var taskTrigger = TriggerBuilder.Create()
                    .WithIdentity("DailyTrigger", "Default")
                    .WithCronSchedule("0 0 8,20 * * ?", x => x.InTimeZone(TimeZoneInfo.Local))
                    .Build();

                await scheduler.ScheduleJob(taskJob, taskTrigger);
                logger.LogInformation("Quartz scheduler started for GenerateTasksJob at 08:00 daily.");

                // Задача для генерации назначений (без расписания, для ручного вызова)
                var assignmentJob = JobBuilder.Create<GenerateAssignmentsJob>()
                    .WithIdentity("GenerateAssignmentsJob", "default")
                    .StoreDurably() // Хранить задачу без триггера
                    .Build();

                await scheduler.AddJob(assignmentJob, false);
                logger.LogInformation("GenerateAssignmentsJob registered for manual triggering.");

                var mainWindow = _serviceProvider.GetService<MainWindow>();
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