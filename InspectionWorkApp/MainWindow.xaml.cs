using InspectionWorkApp.Controllers;
using InspectionWorkApp.Interfaces;
using InspectionWorkApp.Models;
using InspectionWorkApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices; // Для P/Invoke
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop; // Для работы с окнами и хуками
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace InspectionWorkApp
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly IDbContextFactory<YourDbContext> _dbFactory;
        private readonly OperatorService _operatorService;
        private readonly COMController _comController;
        private readonly ILogger<MainWindow> _logger;
        private readonly TimeSpan _dayShiftDueTime = TimeSpan.FromHours(8); // 08:00 для дневной смены
        private readonly TimeSpan _nightShiftDueTime = TimeSpan.FromHours(20); // 20:00 для ночной смены
        private readonly int _koWorkTypeId = 2; // Id для КО работ
        private readonly DateTime _defaultExecutionTime = new DateTime(1900, 1, 1); // Безопасное значение для DATETIME
        private int? _currentRoleId;
        private int? _currentSectorId;
        private readonly ObservableCollection<TaskViewModel> _tasksCollection = new ObservableCollection<TaskViewModel>();
        private readonly AsyncLock _dbLock = new AsyncLock();
        private DateTime _lastSelectionChange = DateTime.MinValue;
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
        private List<TaskViewModel> _allTasks = new List<TaskViewModel>(); // Полный список задач
        private int _currentPage = 1;
        private const int _pageSize = 6; // 6 элементов на страницу
        private bool _isAdminRole; // Свойство для управления доступностью селекторов
        private readonly ILoggerFactory _loggerFactory;
        private IntPtr _keyboardHookId = IntPtr.Zero; // Для хука клавиатуры
        private readonly DispatcherTimer _dbStatusTimer; // Таймер для проверки соединения
        private bool _isDatabaseConnected; // Для отслеживания состояния
        private string _databaseStatusText; // Для текста статуса
        private Brush _databaseStatusColor; // Для цвета текста
        private int? _startupRoleId;
        private string _startupCardNumber;
        private bool _isAutoInitialized = false;  // ← НОВОЕ
        private bool _f8WaitingForConfirm = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsAdminRole
        {
            get => _isAdminRole;
            set
            {
                _isAdminRole = value;
                NotifyPropertyChanged(nameof(IsAdminRole));
            }
        }
        
        public string DatabaseStatusText
        {
            get => _databaseStatusText;
            set
            {
                _databaseStatusText = value;
                NotifyPropertyChanged(nameof(DatabaseStatusText));
            }
        }

        public Brush DatabaseStatusColor
        {
            get => _databaseStatusColor;
            set
            {
                _databaseStatusColor = value;
                NotifyPropertyChanged(nameof(DatabaseStatusColor));
            }
        }
        public MainWindow(IDbContextFactory<YourDbContext> dbFactory, OperatorService operatorService, COMController comController, ILogger<MainWindow> logger, ILoggerFactory loggerFactory)
        {
            InitializeComponent();
            _dbFactory = dbFactory;
            _operatorService = operatorService ?? throw new ArgumentNullException(nameof(operatorService));
            _comController = comController ?? throw new ArgumentNullException(nameof(comController));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbLock = new AsyncLock(_logger);
            _operatorService.OnOperatorChanged += OperatorService_OnOperatorChanged;
            _comController.StateChanged += ComController_StateChanged;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<MainWindow>();

            // Инициализация таймера
            _dbStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // Проверка каждые 10 секунд
            };
            _dbStatusTimer.Tick += async (s, e) => await CheckDatabaseConnectionAsync();
            txtDatabaseStatus.Text = "Проверяется...";
            txtOperatorStatus.Foreground = Brushes.Red;

            // Установить DataContext для привязки
            DataContext = this;
            this.Topmost = true;
            _comController.IsReading = true;
            _logger.LogInformation("MainWindow initialized.");
            _loggerFactory = loggerFactory;
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        // Новый конструктор или метод для получения параметров
        public void SetStartupParameters(int roleId, string cardNumber)
        {
            _startupRoleId = roleId;
            _startupCardNumber = cardNumber;
            _isAutoInitialized = true;  // ← Устанавливаем флаг
            _logger.LogInformation("Startup parameters received: RoleId={RoleId}, CardNumber={CardNumber}", roleId, cardNumber);
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _currentSectorId = await GetCurrentSectorIdAsync();
                _logger.LogInformation("SectorId loaded early: {SectorId}", _currentSectorId);
                // Используем параметры из командной строки, если есть
                if (_startupRoleId.HasValue && !string.IsNullOrEmpty(_startupCardNumber))
                {
                    _currentRoleId = _startupRoleId;
                    await _operatorService.InitializeOperatorAsync(_startupCardNumber);
                    _logger.LogInformation("✓ AUTO-INIT APPLIED: RoleId={RoleId}", _currentRoleId);
                }
                else
                {
                    _logger.LogInformation("No startup parameters, using default initialization");
                }
                // Устанавливаем хук клавиатуры для не-администраторов
                if (_currentRoleId != 4)
                {
                    SetKeyboardHook();
                    HideTaskbar();
                }
                
                _logger.LogInformation("Set _currentSectorId to {SectorId} from GetCurrentSectorIdAsync", _currentSectorId.HasValue ? _currentSectorId.Value.ToString() : "null");
                await LoadCombosAsync();
                //await Dispatcher.InvokeAsync(() =>  // 2. Выбор ПОСЛЕ загрузки
                //{
                //    if (_currentRoleId.HasValue && cmbRole.ItemsSource != null)
                //    {
                //        cmbRole.SelectedValue = _currentRoleId.Value;
                //        _logger.LogInformation("🎉 AUTO-INIT: cmbRole SUCCESSFULLY set to RoleId={RoleId}", _currentRoleId.Value);
                //    }
                //});
                //await LoadTasksAsync();
                UpdateWindowStyleAndRestrictions();

                // Начать проверку соединения
                _dbStatusTimer.Start();
                await CheckDatabaseConnectionAsync(); // Первая проверка при загрузке
                //await Dispatcher.InvokeAsync(() =>
                //{
                //    if (!_isAutoInitialized)
                //    {
                //        cmbRole.SelectedValue = _currentRoleId;
                //    }
                //    else
                //    {
                //        _logger.LogInformation("Auto-init: cmbRole NOT overridden, using RoleId={RoleId}", _currentRoleId);
                //    }
                //});
                // Установка начального состояния кнопки btnOpenAdmin
                if (_operatorService.CurrentOperator != null)
                {
                    using (var db = _dbFactory.CreateDbContext())
                    {
                        var cardNumber = _operatorService.CurrentOperator.CardNumber;
                        //var skudRecord = await db.dic_SKUD
                        //    .Where(s => s.idCard == cardNumber)
                        //    .Select(s => new { s.TORoleId })
                        //    .FirstOrDefaultAsync()
                        //    .ConfigureAwait(false);

                        //_currentRoleId = skudRecord?.TORoleId;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            btnOpenAdmin.IsEnabled = _currentRoleId == 4; // Активна только для администратора
                            btnOpenReports.IsEnabled = _currentRoleId == 4; // Активна для администратора
                            IsAdminRole = _currentRoleId == 4;
                            _logger.LogInformation("Initial btnOpenAdmin.IsEnabled set to {IsEnabled} for RoleId: {RoleId}", btnOpenAdmin.IsEnabled, _currentRoleId);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Window_Loaded");
                MessageBox.Show($"Ошибка при инициализации окна: {ex.Message}");
            }
        }

        private async Task CheckDatabaseConnectionAsync()
        {
            try
            {
                using (var db = _dbFactory.CreateDbContext())
                {
                    bool isConnected = await db.Database.CanConnectAsync();
                    if (isConnected && !_isDatabaseConnected)
                    {
                        _isDatabaseConnected = true;
                        txtDatabaseStatus.Text = "Подключено";
                        txtDatabaseStatus.Foreground = Brushes.Green;
                        _logger.LogInformation("Database connection restored.");
                    }
                    else if (!isConnected && _isDatabaseConnected)
                    {
                        _isDatabaseConnected = false;
                        txtDatabaseStatus.Text = "Разорвано";
                        txtDatabaseStatus.Foreground = Brushes.Red;
                        _logger.LogWarning("Database connection lost.");
                    }
                    else if (!isConnected)
                    {
                        txtDatabaseStatus.Text = "Разорвано";
                        txtDatabaseStatus.Foreground = Brushes.Red;
                    }
                    else
                    {
                        txtDatabaseStatus.Text = "Подключено";
                        txtDatabaseStatus.Foreground = Brushes.Green;
                    }
                }
            }
            catch (Exception ex)
            {
                _isDatabaseConnected = false;
                txtDatabaseStatus.Text = "Разорвано";
                txtDatabaseStatus.Foreground = Brushes.Red;
                _logger.LogError(ex, "Error checking database connection.");
            }
        }

        private void ComController_StateChanged(object sender, COMEventArgs.ReadingDataEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                switch (e.State)
                {
                    case COMControllerParamsModel.COMStates.ReaderConnecting:
                        txtOperatorStatus.Text = "COM-порт: Подключение...";
                        txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Orange;
                        _logger.LogInformation("COM port connecting...");
                        break;
                    case COMControllerParamsModel.COMStates.Detected:
                        txtOperatorStatus.Text = $"COM-порт: Карта считана ({e.CardId})";
                        txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Green;
                        _logger.LogInformation("Card detected: {CardId}", e.CardId);
                        break;
                    case COMControllerParamsModel.COMStates.Removed:
                        txtOperatorStatus.Text = "COM-порт: Карта удалена";
                        txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Orange;
                        _logger.LogInformation("Card removed.");
                        break;
                    case COMControllerParamsModel.COMStates.None:
                        txtOperatorStatus.Text = $"COM-порт: Ошибка ({e.ErrorText})";
                        txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Red;
                        _logger.LogError("COM port error: {ErrorText}", e.ErrorText);
                        break;
                }
            });
        }

        private async Task<int?> GetCurrentSectorIdAsync()
        {
            using (var db = _dbFactory.CreateDbContext())
            {
                try
                {
                    var machineName = Environment.MachineName;
                    _logger.LogInformation("Retrieving sector for computer name: {MachineName}", machineName);

                    var sectorEntry = await db.dic_PCNameSector
                        .Where(p => p.NamePC == machineName)
                        .Select(p => p.Sector)
                        .FirstOrDefaultAsync()
                        .ConfigureAwait(false);

                    if (sectorEntry == null)
                    {
                        _logger.LogWarning("No sector found for computer name: {MachineName}", machineName);
                        return null;
                    }

                    _logger.LogInformation("Found sector {SectorId} for computer name: {MachineName}", sectorEntry, machineName);
                    return sectorEntry;
                }
                finally
                {
                    _logger.LogInformation("Lock released in GetCurrentSectorIdAsync");
                }
            }
        }
        private void UpdateWindowStyleAndRestrictions()
        {
            if (_currentRoleId == 4) // Администратор
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                UnsetKeyboardHook(); // Убираем хук для админа
                ShowTaskbar(); // Показываем панель задач
                _logger.LogInformation("Set WindowStyle to SingleBorderWindow for Admin (RoleId=4)");
            }
            else // Оператор или слесарь
            {
                WindowStyle = WindowStyle.None;
                SetKeyboardHook(); // Устанавливаем хук
                HideTaskbar(); // Скрываем панель задач
                _logger.LogInformation("Set WindowStyle to None for non-Admin (RoleId={RoleId})", _currentRoleId);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Убираем хук при закрытии приложения
            UnsetKeyboardHook();
            ShowTaskbar(); // Восстанавливаем панель задач
            base.OnClosing(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            //if (e.Key == Key.Escape && _currentRoleId != 4)
            //{
            //    _logger.LogInformation("Escape key pressed, closing application.");
            //    Close();
            //}
            if (e.Key == Key.System && e.SystemKey == Key.F4 && _currentRoleId != 4)
            {
                _logger.LogInformation("Alt+F4 blocked for non-admin role.");
                e.Handled = true; // Блокируем Alt+F4
            }
            if (e.Key == Key.F8)
            {
                if (_f8WaitingForConfirm)
                {
                    _logger.LogCritical("Подтверждено: F8 два раза → аварийный выход");
                    Application.Current.Shutdown();
                }
                else
                {
                    _f8WaitingForConfirm = true;
                    Dispatcher.InvokeAsync(async () =>
                    {
                        //txtOperatorStatus.Text = "Нажмите F8 ещё раз для выхода";
                        txtOperatorStatus.Foreground = Brushes.Red;
                        await Task.Delay(3000);
                        _f8WaitingForConfirm = false;
                        txtOperatorStatus.Text = "Вставьте пропуск";
                        txtOperatorStatus.Foreground = Brushes.Gray;
                    });
                }
            }
        }
        

        #region Keyboard Hook and Taskbar Management
        // Windows API для хука клавиатуры
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private void SetKeyboardHook()
        {
            if (_keyboardHookId == IntPtr.Zero)
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                    _logger.LogInformation("Keyboard hook set: {HookId}", _keyboardHookId);
                }
            }
        }

        private void UnsetKeyboardHook()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
                _logger.LogInformation("Keyboard hook unset");
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                // Блокируем клавишу Windows, Alt+Tab, Ctrl+Shift+Esc, Win+D
                if (vkCode == 0x5B || vkCode == 0x5C || // Left/Right Windows Key
                    (vkCode == 0x09 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) || // Alt+Tab
                    (vkCode == 0x1B && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) || // Ctrl+Shift+Esc
                    (vkCode == 0x44 && (Keyboard.Modifiers & ModifierKeys.Windows) != 0)) // Win+D
                {
                    return (IntPtr)1; // Блокируем событие
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void HideTaskbar()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                ShowWindow(taskbar, SW_HIDE);
                _logger.LogInformation("Taskbar hidden");
            }
        }

        private void ShowTaskbar()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                ShowWindow(taskbar, SW_SHOW);
                _logger.LogInformation("Taskbar shown");
            }
        }
        #endregion
        private async void OperatorService_OnOperatorChanged(object sender, EventArgs e)
        {
            _logger.LogInformation("OperatorService_OnOperatorChanged started, ThreadId={ThreadId}, PersonnelNumber={PersonnelNumber}",
                System.Threading.Thread.CurrentThread.ManagedThreadId, _operatorService.CurrentOperator?.PersonnelNumber ?? "null");

            if (_isLoadingTasks)
            {
                _logger.LogInformation("OperatorService_OnOperatorChanged skipped due to ongoing LoadTasksAsync");
                return;
            }

            try
            {
                using (var db = _dbFactory.CreateDbContext())
                {
                    if (_operatorService.CurrentOperator != null)
                    {
                        var cardNumber = _operatorService.CurrentOperator.CardNumber;
                        var skudRecord = await db.dic_SKUD
                            .Where(s => s.idCard == cardNumber)
                            .Select(s => new { s.TORoleId })
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(false);

                        _currentRoleId = skudRecord?.TORoleId;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtOperatorStatus.Text = $"Авторизован: {_operatorService.CurrentOperator.FullName} ({_operatorService.CurrentOperator.PersonnelNumber})";
                            txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Green;
                            btnOpenAdmin.IsEnabled = _currentRoleId == 4; // Активна только для администратора
                            btnOpenReports.IsEnabled = _currentRoleId == 4; // Активна для администратора
                            IsAdminRole = _currentRoleId == 4;
                            UpdateWindowStyleAndRestrictions();
                            if (skudRecord?.TORoleId != null)
                            {
                                if (_currentRoleId.HasValue)
                                {
                                    var roles = cmbRole.ItemsSource as List<Role>;
                                    if (roles != null && roles.Any())
                                    {
                                        var selectedRole = roles.FirstOrDefault(r => r.Id == _currentRoleId.Value);
                                        if (selectedRole != null)
                                        {
                                            cmbRole.SelectedItem = selectedRole;
                                            if (_isAutoInitialized)
                                            {
                                                _logger.LogInformation("🎉 AUTO-INIT: cmbRole set to RoleId={RoleId} from startup", _currentRoleId.Value);
                                            }
                                            else
                                            {
                                                _logger.LogInformation("✓ MANUAL: cmbRole set to RoleId={RoleId} from dic_SKUD for cardNumber: {cardNumber}",
                                                    _currentRoleId.Value, cardNumber);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning("No TORoleId found in dic_SKUD for cardNumber: {cardNumber}", cardNumber);
                                cmbRole.SelectedItem = null;
                            }

                            _logger.LogInformation("IsAdminRole set to {IsAdminRole} and btnOpenAdmin.IsEnabled set to {IsEnabled} for RoleId: {RoleId}", IsAdminRole, btnOpenAdmin.IsEnabled, _currentRoleId);
                        });
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtOperatorStatus.Text = "Вставьте пропуск";
                            txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Red;
                            btnOpenAdmin.IsEnabled = false;
                            btnOpenReports.IsEnabled = false;
                            _currentRoleId = null;
                            cmbRole.SelectedItem = null;
                            IsAdminRole = false;
                            UpdateWindowStyleAndRestrictions();
                            _logger.LogInformation("Operator deauthenticated, btnOpenAdmin.IsEnabled set to False");
                        });
                    }

                    _isLoadingTasks = false;
                    await LoadTasksAsync();
                    _logger.LogInformation("Tasks reloaded after operator change.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OperatorService_OnOperatorChanged");
                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Ошибка при смене оператора: {ex.Message}"));
            }
            finally
            {
                _logger.LogInformation("Lock released in OperatorService_OnOperatorChanged, ThreadId={ThreadId}", System.Threading.Thread.CurrentThread.ManagedThreadId);
            }
        }

        private async Task LoadCombosAsync()
        {
            using (var db = _dbFactory.CreateDbContext())
            {
                try
                {
                    var roles = await db.TORoles.ToListAsync();
                    _logger.LogInformation("Loaded {Count} roles into cmbRole", roles.Count);

                    var sectors = await db.dic_Sector.ToListAsync();
                    _logger.LogInformation("Loaded {Count} sectors into cmbSector", sectors.Count);

                    Dispatcher.Invoke(() =>
                    {
                        cmbRole.ItemsSource = roles;
                        cmbSector.ItemsSource = sectors;

                        if (_currentSectorId.HasValue)
                        {
                            var selectedSector = sectors.FirstOrDefault(s => s.Id == _currentSectorId.Value);
                            if (selectedSector != null)
                            {
                                cmbSector.SelectedItem = selectedSector;
                                _logger.LogInformation("Set cmbSector to SectorId: {SectorId}", _currentSectorId.Value);
                            }
                            else
                            {
                                _logger.LogWarning("SectorId {SectorId} not found in dic_Sector", _currentSectorId.Value);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("GetCurrentSectorIdAsync returned null, no sector selected");
                        }

                        Dispatcher.Invoke(async () =>
                        {
                            var currentOperator = _operatorService.CurrentOperator;
                            if (currentOperator != null)
                            {
                                var cardNumber = currentOperator.CardNumber;
                                var skudRecord = await db.dic_SKUD
                                    .Where(s => s.idCard == cardNumber)
                                    .Select(s => new { s.TORoleId })
                                    .FirstOrDefaultAsync();

                                if (skudRecord?.TORoleId != null)
                                {
                                    _currentRoleId = skudRecord.TORoleId;
                                    IsAdminRole = _currentRoleId == 4; // true только для администратора
                                    _logger.LogInformation("IsAdminRole set to {IsAdminRole} for RoleId: {RoleId}", IsAdminRole, _currentRoleId);

                                    var selectedRole = roles.FirstOrDefault(r => r.Id == _currentRoleId.Value);
                                    if (selectedRole != null)
                                    {
                                        cmbRole.SelectedItem = selectedRole;
                                        _logger.LogInformation("Set cmbRole to TORoleId: {TORoleId} for cardNumber: {cardNumber}", _currentRoleId.Value, cardNumber);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("TORoleId {TORoleId} not found in TORoles for cardNumber: {cardNumber}", skudRecord.TORoleId, cardNumber);
                                    }
                                }
                                else
                                {
                                    _currentRoleId = null;
                                    cmbRole.SelectedItem = null;
                                    IsAdminRole = false;
                                    _logger.LogWarning("No TORoleId found in dic_SKUD for cardNumber: {cardNumber}", cardNumber);
                                }
                                //if (_currentRoleId.HasValue && _isAutoInitialized)
                                //{
                                //    var rolesList = cmbRole.ItemsSource as List<Role>;
                                //    if (rolesList != null && rolesList.Any())
                                //    {
                                //        var selectedRole = rolesList.FirstOrDefault(r => r.Id == _currentRoleId.Value);
                                //        if (selectedRole != null)
                                //        {
                                //            cmbRole.SelectedItem = selectedRole;
                                //            _logger.LogInformation("🎉 ROLE SET IN LoadCombosAsync: RoleId={RoleId}", _currentRoleId.Value);
                                //            return; // ✅ Выход - роль установлена!
                                //        }
                                //    }
                                //    _logger.LogWarning("Failed to set role in LoadCombosAsync: RoleId={RoleId}", _currentRoleId);
                                //}
                            }
                            else
                            {
                                _currentRoleId = null;
                                cmbRole.SelectedItem = null;
                                IsAdminRole = false;
                                _logger.LogWarning("No current operator or no TORoleId for cardNumber: {cardNumber}", currentOperator?.CardNumber ?? "null");
                            }
                        });
                        // ✅ ✅ ✅ КРИТИЧНО: УСТАНОВКА РОЛИ ПОСЛЕ ЗАГРУЗКИ ItemsSource
                        Dispatcher.InvokeAsync(async () =>
                        {
                            if (_currentRoleId.HasValue && cmbRole.ItemsSource != null)
                            {
                                var rolesList = cmbRole.ItemsSource as List<Role>;
                                var selectedRole = rolesList?.FirstOrDefault(r => r.Id == _currentRoleId.Value);
                                if (selectedRole != null)
                                {
                                    cmbRole.SelectedItem = selectedRole;
                                    _logger.LogInformation("🎉 LoadCombosAsync: cmbRole SET to RoleId={RoleId}", _currentRoleId.Value);
                                }
                            }
                        });
                        // Обработчики событий для ручного выбора
                        cmbRole.SelectionChanged += (s, e) =>
                        {
                            _currentRoleId = (cmbRole.SelectedItem as Role)?.Id;
                            _logger.LogInformation("Role changed to RoleId: {RoleId}", _currentRoleId);
                            Task.Run(async () => await LoadTasksAsync());
                        };
                        cmbSector.SelectionChanged += (s, e) =>
                        {
                            _currentSectorId = (cmbSector.SelectedItem as Sector)?.Id;
                            _logger.LogInformation("Sector changed to SectorId: {SectorId}", _currentSectorId);
                            Task.Run(async () => await LoadTasksAsync());
                        };
                    });

                    _logger.LogInformation("Combos loaded successfully.");
                }
                finally
                {
                    _logger.LogInformation("Lock released in LoadCombosAsync");
                }
            }
        }

        private bool _isLoadingTasks = false;

        private async Task LoadTasksAsync()
        {
            if (_isLoadingTasks)
            {
                _logger.LogInformation("LoadTasksAsync skipped due to ongoing operation, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
                return;
            }
            _isLoadingTasks = true;
            _logger.LogInformation("LoadTasksAsync started, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            try
            {
                using (var db = _dbFactory.CreateDbContext())
                {
                    // Если RoleId = 4 (администратор), отображаем пустой список задач
                    if (_currentRoleId == 4)
                    {
                        _logger.LogInformation("Нет задач для администратора (RoleId=4)");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _allTasks = new List<TaskViewModel>();
                            UpdatePagedTasks();
                            if (dgTasks.ItemsSource == null)
                            {
                                dgTasks.ItemsSource = _tasksCollection;
                            }
                            _logger.LogInformation("Отображено 0 задач для RoleId=4");
                        });
                        return;
                    }

                    // Фильтрация задач для RoleId=1 или RoleId=2
                    var now = DateTime.Now;
                    var today = now.Date;
                    DateTime shiftStart;
                    if (now.Hour >= 8 && now.Hour < 20)
                    {
                        shiftStart = today.AddHours(8);
                    }
                    else if (now.Hour >= 0 && now.Hour < 8)
                    {
                        shiftStart = today.AddDays(-1).AddHours(20);
                    }
                    else
                    {
                        shiftStart = today.AddHours(20);
                    }
                    _logger.LogInformation("ShiftStart calculated: {ShiftStart}", shiftStart);

                    var assignmentsQuery = db.TOWorkAssignments
                        .AsNoTracking()
                        .Include(a => a.Work)
                        .Include(a => a.WorkType)
                        .Include(a => a.Freq)
                        .Where(a => !a.IsCanceled);

                    if (_currentRoleId.HasValue && (_currentRoleId == 1 || _currentRoleId == 2))
                    {
                        assignmentsQuery = assignmentsQuery.Where(a => a.RoleId == _currentRoleId.Value);
                        _logger.LogInformation("Фильтрация задач по RoleId: {RoleId}", _currentRoleId.Value);
                    }
                    else
                    {
                        // Если RoleId отсутствует или недопустим, отображаем пустой список
                        _logger.LogInformation("Нет задач из-за отсутствия или недопустимого RoleId: {RoleId}", _currentRoleId);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _allTasks = new List<TaskViewModel>();
                            UpdatePagedTasks();
                            if (dgTasks.ItemsSource == null)
                            {
                                dgTasks.ItemsSource = _tasksCollection;
                            }
                            _logger.LogInformation("Отображено 0 задач из-за недопустимого RoleId");
                        });
                        return;
                    }

                    if (_currentSectorId.HasValue)
                    {
                        assignmentsQuery = assignmentsQuery.Where(a => a.SectorId == _currentSectorId.Value);
                        _logger.LogInformation("Фильтрация задач по SectorId: {SectorId}", _currentSectorId.Value);
                    }

                    var assignments = await assignmentsQuery.ToListAsync().ConfigureAwait(false);
                    var tasks = new List<TaskViewModel>();

                    var taskViewModelLogger = _loggerFactory.CreateLogger<TaskViewModel>();

                    foreach (var a in assignments)
                    {
                        var freq = a.Freq;
                        var dueDateTime = a.LastExecTime;
                        DateTime nextDue;

                        if (freq.Id == 1)
                        {
                            nextDue = shiftStart;
                        }
                        else
                        {
                            var days = freq.Id switch
                            {
                                2 => freq.IntervalDay ?? 0,
                                3 => freq.IntervalDay ?? 0,
                                4 => freq.IntervalDay ?? 0,
                                5 => freq.IntervalDay ?? 0,
                                _ => 7
                            };
                            nextDue = dueDateTime == _defaultExecutionTime
                                ? today
                                : (dueDateTime.HasValue ? dueDateTime.Value.AddDays((double)days) : today);
                        }

                        var execution = await db.TOExecutions
                            .AsNoTracking()
                            .Where(e => e.AssignmentId == a.Id && e.DueDateTime == shiftStart)
                            .Select(e => new { e.Status, e.ExecutionTime })
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(false);
                        _logger.LogInformation("Execution check for AssignmentId={AssignmentId}, DueDateTime={DueDateTime}, Found={Found}", a.Id, shiftStart, execution != null);

                        if ((freq.Id == 1 || nextDue <= now) && execution == null)
                        {
                            var statusName = "Требуется выполнить!";

                            tasks.Add(new TaskViewModel(taskViewModelLogger)
                            {
                                Id = a.Id,
                                WorkName = a.Work?.WorkName ?? "Unknown",
                                WorkType = a.WorkType?.WorkType ?? "Unknown",
                                DueDateTime = nextDue,
                                StatusName = statusName,
                                ExecutionTime = null,
                                IsUnprocessed = true
                            });

                            _logger.LogInformation("Task AssignmentId={AssignmentId}: WorkName={WorkName}, Status={StatusName}, DueDateTime={DueDateTime}, ExecutionTime={ExecutionTime}",
                                a.Id, a.Work?.WorkName, statusName, nextDue, null);
                        }
                        else
                        {
                            _logger.LogInformation("Task excluded: AssignmentId={AssignmentId}, WorkName={WorkName}, Reason=Task already processed or nextDue not due",
                                a.Id, a.Work?.WorkName);
                        }
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        _allTasks = tasks;
                        UpdatePagedTasks(); // Сначала обновляем пагинацию (чтобы UI показал пустой список)

                        if (_allTasks.Count == 0 && _operatorService.CurrentOperator != null)
                        {
                            _logger.LogInformation("All tasks completed for current shift, closing application");
                            //MessageBox.Show("Все задачи обработаны. Программа закрывается.");
                            this.Close(); // Автоматическое закрытие программы
                        }

                        if (dgTasks.ItemsSource == null)
                        {
                            dgTasks.ItemsSource = _tasksCollection;
                        }
                        _logger.LogInformation("Loaded {Count} tasks into dgTasks", _allTasks.Count);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tasks");
                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Ошибка при загрузке задач: {ex.Message}"));
            }
            finally
            {
                _isLoadingTasks = false;
                _logger.LogInformation("Lock released in LoadTasksAsync, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            }
        }

        private void UpdatePagedTasks()
        {
            _tasksCollection.Clear();
            var pagedTasks = _allTasks.Skip((_currentPage - 1) * _pageSize).Take(_pageSize);
            foreach (var task in pagedTasks)
            {
                _tasksCollection.Add(task);
            }

            int totalPages = (int)Math.Ceiling((double)_allTasks.Count / _pageSize);
            txtPageInfo.Text = $"{_currentPage} из {totalPages}";
            btnPrevPage.IsEnabled = _currentPage > 1;
            btnNextPage.IsEnabled = _currentPage < totalPages;
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                UpdatePagedTasks();
            }
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)_allTasks.Count / _pageSize);
            if (_currentPage < totalPages)
            {
                _currentPage++;
                UpdatePagedTasks();
            }
        }

        private async void BtnMarkCompletedPerTask_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || button.Tag == null) return;

            int assignmentId = (int)button.Tag;
            var task = button.DataContext as TaskViewModel;
            if (task != null)
            {
                _logger.LogInformation("Marking task completed: AssignmentId={AssignmentId}, Comment={Comment}", assignmentId, task.Comment ?? "null");
                await MarkTaskCompletedAsync(assignmentId, task.Comment);
            }
            else
            {
                _logger.LogWarning("TaskViewModel is null in BtnMarkCompletedPerTask_Click for AssignmentId={AssignmentId}", assignmentId);
                MessageBox.Show("Не удалось определить задачу.");
            }
        }

        private async void BtnCancelTaskPerTask_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || button.Tag == null) return;

            int assignmentId = (int)button.Tag;
            var task = button.DataContext as TaskViewModel;
            if (task != null)
            {
                _logger.LogInformation("Opening FailureReasonWindow for task: AssignmentId={AssignmentId}", assignmentId);

                var failureReasonWindow = new FailureReasonWindow(_dbFactory);
                failureReasonWindow.Owner = this;
                bool? result = failureReasonWindow.ShowDialog();

                if (result == true && !string.IsNullOrEmpty(failureReasonWindow.SelectedReason))
                {
                    string comment = task.Comment != null
                        ? $"{failureReasonWindow.SelectedReason}{task.Comment}"
                        : $"{failureReasonWindow.SelectedReason}";
                    _logger.LogInformation("Selected reason for task cancellation: AssignmentId={AssignmentId}, Reason={Reason}, FullComment={Comment}", assignmentId, failureReasonWindow.SelectedReason, comment);
                    await CancelTaskAsync(assignmentId, comment);
                }
                else
                {
                    _logger.LogInformation("Task cancellation aborted: AssignmentId={AssignmentId}, Reason=User cancelled or no reason selected", assignmentId);
                }
            }
            else
            {
                _logger.LogWarning("TaskViewModel is null in BtnCancelTaskPerTask_Click for AssignmentId={AssignmentId}", assignmentId);
                MessageBox.Show("Не удалось определить задачу.");
            }
        }

        private async Task MarkTaskCompletedAsync(int assignmentId, string comment)
        {
            _logger.LogInformation("MarkTaskCompletedAsync started for AssignmentId={AssignmentId}, Comment={Comment}", assignmentId, comment ?? "null");

            try
            {
                if (_operatorService.CurrentOperator == null)
                {
                    _logger.LogWarning("Attempt to mark task completed without operator authentication.");
                    MessageBox.Show("Авторизуйтесь, считав карту!");
                    return;
                }

                using (var db = _dbFactory.CreateDbContext())
                {
                    var operatorId = await _operatorService.GetOperatorIdAsync(_operatorService.CurrentOperator.PersonnelNumber).ConfigureAwait(false);
                    if (operatorId == null)
                    {
                        _logger.LogWarning("OperatorId not found for personnelNumber: {PersonnelNumber}", _operatorService.CurrentOperator.PersonnelNumber);
                        MessageBox.Show("Не удалось определить ID оператора.");
                        return;
                    }

                    var now = DateTime.Now;
                    var today = now.Date;
                    DateTime shiftStart = now.Hour >= 8 && now.Hour < 20 ? today.AddHours(8) : (now.Hour >= 0 && now.Hour < 8 ? today.AddDays(-1).AddHours(20) : today.AddHours(20));

                    var existingExecution = await db.TOExecutions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.AssignmentId == assignmentId && e.DueDateTime == shiftStart)
                        .ConfigureAwait(false);

                    if (existingExecution != null)
                    {
                        _logger.LogWarning("Task already processed for AssignmentId={AssignmentId}", assignmentId);
                        MessageBox.Show("Задача уже обработана!");
                        return;
                    }

                    // Используем стратегию выполнения для транзакции
                    var strategy = db.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        using (var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false))
                        {
                            try
                            {
                                var execution = new Execution
                                {
                                    AssignmentId = assignmentId,
                                    OperatorId = operatorId,
                                    ExecutionTime = now,
                                    Status = 1,
                                    DueDateTime = shiftStart,
                                    Comment = comment
                                };
                                db.TOExecutions.Add(execution);

                                var assignment = await db.TOWorkAssignments
                                    .FirstOrDefaultAsync(a => a.Id == assignmentId)
                                    .ConfigureAwait(false);
                                if (assignment != null)
                                {
                                    assignment.LastExecTime = shiftStart;
                                }

                                await db.SaveChangesAsync().ConfigureAwait(false);
                                await transaction.CommitAsync().ConfigureAwait(false);

                                _logger.LogInformation("Task marked as completed for AssignmentId={AssignmentId}, Comment={Comment}", assignmentId, comment ?? "null");
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync().ConfigureAwait(false);
                                _logger.LogError(ex, "Error marking task completed for AssignmentId={AssignmentId}", assignmentId);
                                MessageBox.Show($"Ошибка: {ex.Message}");
                                throw; // Важно пробросить исключение, чтобы strategy могла повторить попытку
                            }
                        }
                    });

                    await LoadTasksAsync();
                }
            }
            finally
            {
                _logger.LogInformation("MarkTaskCompletedAsync completed for AssignmentId={AssignmentId}", assignmentId);
            }
        }

        private async Task CancelTaskAsync(int assignmentId, string comment)
        {
            _logger.LogInformation("CancelTaskAsync started for AssignmentId={AssignmentId}, Comment={Comment}", assignmentId, comment ?? "null");

            try
            {
                if (_operatorService.CurrentOperator == null)
                {
                    _logger.LogWarning("Attempt to cancel task without operator authentication.");
                    MessageBox.Show("Авторизуйтесь, считав карту!");
                    return;
                }

                using (var db = _dbFactory.CreateDbContext())
                {
                    var operatorId = await _operatorService.GetOperatorIdAsync(_operatorService.CurrentOperator.PersonnelNumber).ConfigureAwait(false);
                    if (operatorId == null)
                    {
                        _logger.LogWarning("OperatorId not found for personnelNumber: {PersonnelNumber}", _operatorService.CurrentOperator.PersonnelNumber);
                        MessageBox.Show("Не удалось определить ID оператора.");
                        return;
                    }

                    var now = DateTime.Now;
                    var today = now.Date;
                    DateTime shiftStart = now.Hour >= 8 && now.Hour < 20 ? today.AddHours(8) : (now.Hour >= 0 && now.Hour < 8 ? today.AddDays(-1).AddHours(20) : today.AddHours(20));

                    var existingExecution = await db.TOExecutions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.AssignmentId == assignmentId && e.DueDateTime == shiftStart)
                        .ConfigureAwait(false);

                    if (existingExecution != null)
                    {
                        _logger.LogWarning("Task already processed for AssignmentId={AssignmentId}", assignmentId);
                        MessageBox.Show("Задача уже обработана!");
                        return;
                    }

                    // Используем стратегию выполнения для транзакции
                    var strategy = db.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        using (var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false))
                        {
                            try
                            {
                                var execution = new Execution
                                {
                                    AssignmentId = assignmentId,
                                    OperatorId = operatorId,
                                    ExecutionTime = now,
                                    Status = 2,
                                    DueDateTime = shiftStart,
                                    Comment = comment
                                };
                                db.TOExecutions.Add(execution);

                                var assignment = await db.TOWorkAssignments
                                    .FirstOrDefaultAsync(a => a.Id == assignmentId)
                                    .ConfigureAwait(false);
                                if (assignment != null)
                                {
                                    assignment.LastExecTime = shiftStart;
                                }

                                await db.SaveChangesAsync().ConfigureAwait(false);
                                await transaction.CommitAsync().ConfigureAwait(false);

                                _logger.LogInformation("Task canceled for AssignmentId={AssignmentId}, Comment={Comment}", assignmentId, comment ?? "null");



                                await LoadTasksAsync();
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync().ConfigureAwait(false);
                                _logger.LogError(ex, "Error canceling task for AssignmentId={AssignmentId}", assignmentId);
                                MessageBox.Show($"Ошибка: {ex.Message}");
                                throw; // Пробрасываем исключение для retry
                            }
                        }
                    });
                }
            }
            finally
            {
                _logger.LogInformation("CancelTaskAsync completed for AssignmentId={AssignmentId}", assignmentId);
            }
        }

        private async void BtnOpenAdmin_Click(object sender, RoutedEventArgs e)
        {
            this.Topmost = false;
            _logger.LogInformation("BtnOpenAdmin_Click started, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            using (var db = _dbFactory.CreateDbContext())
            {
                try
                {
                    if (_operatorService.CurrentOperator == null)
                    {
                        MessageBox.Show("Авторизуйтесь, считав карту!");
                        _logger.LogWarning("Attempt to open admin panel without operator authentication.");
                        return;
                    }

                    var adminWindow = new AdminWindow(db, _operatorService);
                    adminWindow.ShowDialog();
                    await LoadTasksAsync();
                    _logger.LogInformation("Admin panel opened.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error opening admin panel.");
                    MessageBox.Show($"Ошибка при открытии админ-панели: {ex.Message}");
                }
                finally
                {
                    _logger.LogInformation("Lock released in BtnOpenAdmin_Click");
                }
            }
        }
        private void BtnOpenReports_Click(object sender, RoutedEventArgs e)
        {
            this.Topmost = false;
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }
            using (var db = _dbFactory.CreateDbContext())
            {
                var reportWindow = new ReportWindow(db);
                reportWindow.ShowDialog();
            }
        }
    }
    public class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ILogger<MainWindow> _logger;

        public AsyncLock(ILogger<MainWindow> logger = null)
        {
            _logger = logger;
        }

        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Attempting to acquire lock in AsyncLock, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Lock acquired in AsyncLock, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            return new ReleaseLock(_semaphore, _logger);
        }

        private class ReleaseLock : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly ILogger<MainWindow> _logger;
            private bool _disposed;

            public ReleaseLock(SemaphoreSlim semaphore, ILogger<MainWindow> logger)
            {
                _semaphore = semaphore;
                _logger = logger;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                    _disposed = true;
                    _logger?.LogInformation("Lock released in AsyncLock, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
                }
            }
        }
    }
}