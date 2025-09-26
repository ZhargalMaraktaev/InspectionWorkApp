using InspectionWorkApp.Controllers;
using InspectionWorkApp.Interfaces;
using InspectionWorkApp.Models;
using InspectionWorkApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Animation;

namespace InspectionWorkApp
{
    public partial class MainWindow : Window
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
        public MainWindow(IDbContextFactory<YourDbContext> dbFactory, OperatorService operatorService, COMController comController, ILogger<MainWindow> logger)
        {
            InitializeComponent();
            _dbFactory = dbFactory;
            _operatorService = operatorService ?? throw new ArgumentNullException(nameof(operatorService));
            _comController = comController ?? throw new ArgumentNullException(nameof(comController));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbLock = new AsyncLock(_logger); // Передаём логгер
            // Подписка на события
            _operatorService.OnOperatorChanged += OperatorService_OnOperatorChanged;
            _comController.StateChanged += ComController_StateChanged;

            // Запуск чтения с COM-порта
            _comController.IsReading = true;
            _logger.LogInformation("MainWindow initialized.");

            // Удалено: _currentSectorId = Task.Run(GetCurrentSectorIdAsync).Result;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _currentSectorId = await GetCurrentSectorIdAsync();
                _logger.LogInformation("Set _currentSectorId to {SectorId} from GetCurrentSectorIdAsync", _currentSectorId.HasValue ? _currentSectorId.Value.ToString() : "null"); 
                await LoadCombosAsync();
                await LoadTasksAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Window_Loaded");
                MessageBox.Show($"Ошибка при инициализации окна: {ex.Message}");
            }
        }
        private async void DgTasks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var now = DateTime.Now;
            if (now - _lastSelectionChange < _debounceInterval)
            {
                _logger.LogInformation("DgTasks_SelectionChanged skipped due to debounce, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
                return;
            }
            _lastSelectionChange = now;

            _logger.LogInformation("DgTasks_SelectionChanged started, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);

            try
            {
                var selectedTasks = dgTasks.SelectedItems.Cast<TaskViewModel>().ToList();
                _logger.LogInformation("Selected tasks: {TaskIds}", string.Join(", ", selectedTasks.Select(t => t.Id)));

                if (!selectedTasks.Any())
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        btnMarkCompleted.IsEnabled = false;
                        btnCancelTask.IsEnabled = false;
                        _logger.LogInformation("No tasks selected, buttons disabled");
                    });
                    return;
                }

                var assignmentIds = selectedTasks.Select(t => t.Id).ToList();
                _logger.LogInformation("AssignmentIds: {AssignmentIds}", string.Join(", ", assignmentIds));
                var jsonAssignmentIds = System.Text.Json.JsonSerializer.Serialize(assignmentIds);
                _logger.LogInformation("JSON for assignmentIds: {Json}", jsonAssignmentIds);

                if (!assignmentIds.Any())
                {
                    _logger.LogWarning("No valid task IDs selected, skipping query.");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        btnMarkCompleted.IsEnabled = false;
                        btnCancelTask.IsEnabled = false;
                    });
                    return;
                }

                bool hasUnprocessedTasks;
                using (var db = _dbFactory.CreateDbContext())
                {
 
                    var today = now.Date;
                    DateTime shiftStart = now.Hour >= 8 && now.Hour < 20 ? today.AddHours(8) : today.AddHours(20);

                    var executions = new List<object>();
                    foreach (var id in assignmentIds)
                    {
                        var execution = await db.TOExecutions
                            .AsNoTracking()
                            .Where(e => e.AssignmentId == id && e.DueDateTime == shiftStart)
                            .Select(e => new { e.AssignmentId, e.OperatorId, e.ExecutionTime })
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(true); // Возвращаемся в UI-поток
                        if (execution != null)
                            executions.Add(execution);
                    }
                    var executionsDict = executions.ToDictionary(
                        e => (int)e.GetType().GetProperty("AssignmentId").GetValue(e),
                        e => e);

                    hasUnprocessedTasks = selectedTasks.Any(task =>
                    {
                        executionsDict.TryGetValue(task.Id, out var execution);
                        return execution == null ||
                               execution.GetType().GetProperty("OperatorId").GetValue(execution) == null ||
                               execution.GetType().GetProperty("ExecutionTime").GetValue(execution) == null;
                    });
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    btnMarkCompleted.IsEnabled = hasUnprocessedTasks;
                    btnCancelTask.IsEnabled = hasUnprocessedTasks;
                    _logger.LogInformation("Selection changed: {TaskCount} tasks selected, HasUnprocessedTasks={HasUnprocessed}, ButtonsEnabled={ButtonsEnabled}",
                        selectedTasks.Count, hasUnprocessedTasks, hasUnprocessedTasks);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DgTasks_SelectionChanged: {Message}, StackTrace: {StackTrace}, InnerException: {InnerException}",
                    ex.Message, ex.StackTrace, ex.InnerException?.Message);
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка при обработке выбора задач: {ex.Message}\nInner: {ex.InnerException?.Message}");
                    btnMarkCompleted.IsEnabled = false;
                    btnCancelTask.IsEnabled = false;
                });
            }
            finally
            {
                _logger.LogInformation("Lock released in DgTasks_SelectionChanged, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
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
        private async void OperatorService_OnOperatorChanged(object sender, EventArgs e)
        {
            _logger.LogInformation("OperatorService_OnOperatorChanged started, ThreadId={ThreadId}, PersonnelNumber={PersonnelNumber}",
                Thread.CurrentThread.ManagedThreadId, _operatorService.CurrentOperator?.PersonnelNumber ?? "null");

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
                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtOperatorStatus.Text = $"Авторизован: {_operatorService.CurrentOperator.FullName} ({_operatorService.CurrentOperator.PersonnelNumber})";
                            txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Green;
                            btnMarkCompleted.IsEnabled = true;
                            btnCancelTask.IsEnabled = true;
                            btnOpenAdmin.IsEnabled = true;
                        });

                        var cardNumber = _operatorService.CurrentOperator.CardNumber;
                        var skudRecord = await db.dic_SKUD
                            .Where(s => s.IdCard == cardNumber)
                            .Select(s => new { s.TORoleId })
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(false);

                        if (skudRecord?.TORoleId != null)
                        {
                            _currentRoleId = skudRecord.TORoleId;
                            await Dispatcher.InvokeAsync(() =>
                            {
                                var roles = cmbRole.ItemsSource as List<Role>;
                                var selectedRole = roles?.FirstOrDefault(r => r.Id == _currentRoleId.Value);
                                if (selectedRole != null)
                                {
                                    cmbRole.SelectedItem = selectedRole;
                                    _logger.LogInformation("Set cmbRole to TORoleId: {TORoleId} for cardNumber: {cardNumber}",
                                        _currentRoleId.Value, cardNumber);
                                }
                                else
                                {
                                    _logger.LogWarning("TORoleId {TORoleId} not found in TORoles for cardNumber: {cardNumber}",
                                        skudRecord.TORoleId, cardNumber);
                                }
                            });
                        }
                        else
                        {
                            _logger.LogWarning("No TORoleId found in dic_SKUD for cardNumber: {cardNumber}", cardNumber);
                        }
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtOperatorStatus.Text = "Не авторизован";
                            txtOperatorStatus.Foreground = System.Windows.Media.Brushes.Red;
                            btnMarkCompleted.IsEnabled = false;
                            btnCancelTask.IsEnabled = false;
                            btnOpenAdmin.IsEnabled = false;
                            _currentRoleId = null;
                            cmbRole.SelectedItem = null;
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
                _logger.LogInformation("Lock released in OperatorService_OnOperatorChanged, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
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

                        // Получаем текущий TORoleId из CurrentOperator
                        Dispatcher.Invoke(async () =>
                        {
                            // Получаем текущий TORoleId из CurrentOperator
                            var currentOperator = _operatorService.CurrentOperator;
                            if (currentOperator != null /* && currentOperator.TORoleId.HasValue */)
                            {
                                // Получаем TORoleId из dic_SKUD, если оператор авторизован
                                var cardNumber = currentOperator.CardNumber;
                                var skudRecord = await db.dic_SKUD
                                    .Where(s => s.IdCard == cardNumber)
                                    .Select(s => new { s.TORoleId })
                                    .FirstOrDefaultAsync();

                                if (skudRecord?.TORoleId != null)
                                {
                                    _currentRoleId = skudRecord.TORoleId;
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
                                    _logger.LogWarning("No TORoleId found in dic_SKUD for cardNumber: {cardNumber}", cardNumber);
                                }
                            }
                            else
                            {
                                _currentRoleId = null;
                                cmbRole.SelectedItem = null;
                                _logger.LogWarning("No current operator or no TORoleId for cardNumber: {cardNumber}", currentOperator?.CardNumber ?? "null");
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

                    if (_currentRoleId.HasValue)
                    {
                        assignmentsQuery = assignmentsQuery.Where(a => a.RoleId == _currentRoleId.Value);
                        _logger.LogInformation("Filtering tasks by RoleId: {RoleId}", _currentRoleId.Value);
                    }

                    if (_currentSectorId.HasValue)
                    {
                        assignmentsQuery = assignmentsQuery.Where(a => a.SectorId == _currentSectorId.Value);
                        _logger.LogInformation("Filtering tasks by SectorId: {SectorId}", _currentSectorId.Value);
                    }

                    var assignments = await assignmentsQuery.ToListAsync().ConfigureAwait(false);
                    var tasks = new List<TaskViewModel>();

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
                                2 => 7,
                                3 => 14,
                                4 => 30,
                                5 => 365,
                                _ => 7
                            };
                            nextDue = dueDateTime == _defaultExecutionTime
                                ? today
                                : (dueDateTime.HasValue ? dueDateTime.Value.AddDays(days) : today);
                        }

                        if (freq.Id == 1 || nextDue <= now)
                        {
                            var execution = await db.TOExecutions
                                .AsNoTracking()
                                .Where(e => e.AssignmentId == a.Id && e.DueDateTime == shiftStart)
                                .Select(e => new { e.Status, e.ExecutionTime })
                                .FirstOrDefaultAsync()
                                .ConfigureAwait(false);

                            string statusName = execution?.Status switch
                            {
                                1 => "Выполнена",
                                2 => "Отменена",
                                _ => "Не выполнена"
                            };

                            tasks.Add(new TaskViewModel
                            {
                                Id = a.Id,
                                WorkName = a.Work?.WorkName ?? "Unknown",
                                WorkType = a.WorkType?.WorkType ?? "Unknown",
                                DueDateTime = nextDue,
                                StatusName = statusName,
                                ExecutionTime = execution?.ExecutionTime
                            });

                            _logger.LogInformation("Task AssignmentId={AssignmentId}: WorkName={WorkName}, Status={StatusName}, DueDateTime={DueDateTime}, ExecutionTime={ExecutionTime}",
                                a.Id, a.Work?.WorkName, statusName, nextDue, execution?.ExecutionTime);
                        }
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        _tasksCollection.Clear();
                        foreach (var task in tasks)
                        {
                            _tasksCollection.Add(task);
                        }
                        if (dgTasks.ItemsSource == null)
                        {
                            dgTasks.ItemsSource = _tasksCollection;
                        }
                        _logger.LogInformation("Loaded {Count} tasks into dgTasks", _tasksCollection.Count);
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

        private async void BtnMarkCompleted_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInformation("BtnMarkCompleted_Click started, ThreadId={ThreadId}, Sender={SenderType}", Thread.CurrentThread.ManagedThreadId, sender.GetType().Name);
            await Dispatcher.InvokeAsync(() => btnMarkCompleted.IsEnabled = false);

            try
            {
                if (_operatorService.CurrentOperator == null)
                {
                    _logger.LogWarning("Attempt to mark tasks completed without operator authentication.");
                    await Dispatcher.InvokeAsync(() => MessageBox.Show("Авторизуйтесь, считав карту!"));
                    return;
                }

                var selectedTasks = await Dispatcher.InvokeAsync(() => dgTasks.SelectedItems.Cast<TaskViewModel>().ToList());
                if (!selectedTasks.Any())
                {
                    _logger.LogWarning("No tasks selected in dgTasks when attempting to mark completed.");
                    await Dispatcher.InvokeAsync(() => MessageBox.Show("Выберите хотя бы одну задачу в таблице!"));
                    return;
                }

                using (var db = _dbFactory.CreateDbContext())
                {
                    try
                    {
                        var operatorId = await _operatorService.GetOperatorIdAsync(_operatorService.CurrentOperator.PersonnelNumber).ConfigureAwait(false);
                        if (operatorId == null)
                        {
                            _logger.LogWarning("OperatorId not found for personnelNumber: {PersonnelNumber}", _operatorService.CurrentOperator.PersonnelNumber);
                            await Dispatcher.InvokeAsync(() => MessageBox.Show("Не удалось определить ID оператора."));
                            return;
                        }

                        var now = DateTime.Now;
                        var today = now.Date;
                        DateTime shiftStart = now.Hour >= 8 && now.Hour < 20 ? today.AddHours(8) : today.AddHours(20);
                        _logger.LogInformation("ShiftStart calculated: {ShiftStart}", shiftStart);

                        using (var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false))
                        {
                            try
                            {
                                int completedCount = 0;
                                foreach (var task in selectedTasks)
                                {
                                    var assignmentId = task.Id;
                                    _logger.LogInformation("Processing task completion for AssignmentId={AssignmentId}", assignmentId);

                                    var existingExecution = await db.TOExecutions
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(e => e.AssignmentId == assignmentId && e.DueDateTime == shiftStart)
                                        .ConfigureAwait(false);
                                    if (existingExecution != null)
                                    {
                                        _logger.LogWarning("Task already processed for AssignmentId={AssignmentId}, DueDateTime={DueDateTime}, Status={Status}",
                                            assignmentId, existingExecution.DueDateTime, existingExecution.Status);
                                        continue;
                                    }

                                    var execution = new Execution
                                    {
                                        AssignmentId = assignmentId,
                                        OperatorId = operatorId,
                                        ExecutionTime = now,
                                        Status = 1,
                                        DueDateTime = shiftStart
                                    };
                                    db.TOExecutions.Add(execution);

                                    var assignment = await db.TOWorkAssignments
                                        .FirstOrDefaultAsync(a => a.Id == assignmentId)
                                        .ConfigureAwait(false);
                                    if (assignment == null)
                                    {
                                        _logger.LogError("Assignment not found for Id: {AssignmentId}", assignmentId);
                                        continue;
                                    }

                                    assignment.LastExecTime = shiftStart;
                                    completedCount++;
                                }

                                await db.SaveChangesAsync().ConfigureAwait(false);
                                await transaction.CommitAsync().ConfigureAwait(false);

                                var message = completedCount == 1 ? "Задача отмечена как выполненная!" : $"Отмечено как выполненные: {completedCount} задач(и)!";
                                await Dispatcher.InvokeAsync(() => MessageBox.Show(message));
                                _logger.LogInformation("Tasks completed: Count={Count}, OperatorId={OperatorId}, ShiftStart={ShiftStart}", completedCount, operatorId, shiftStart);

                                _isLoadingTasks = false;
                                await LoadTasksAsync();
                            }
                            catch (DbUpdateException ex)
                            {
                                await transaction.RollbackAsync().ConfigureAwait(false);
                                _logger.LogError(ex, "Error completing tasks");
                                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Ошибка при сохранении: {ex.InnerException?.Message ?? ex.Message}"));
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync().ConfigureAwait(false);
                                _logger.LogError(ex, "Unexpected error completing tasks");
                                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Непредвиденная ошибка: {ex.Message}"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error in BtnMarkCompleted_Click");
                        await Dispatcher.InvokeAsync(() => MessageBox.Show($"Ошибка: {ex.Message}"));
                    }
                }
            }
            finally
            {
                await Dispatcher.InvokeAsync(() => btnMarkCompleted.IsEnabled = true);
                _logger.LogInformation("BtnMarkCompleted_Click completed, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            }
        }

        private async void BtnCancelTask_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInformation("BtnCancelTask_Click started, ThreadId={ThreadId}, Sender={SenderType}", Thread.CurrentThread.ManagedThreadId, sender.GetType().Name);
            await Dispatcher.InvokeAsync(() => btnCancelTask.IsEnabled = false);

            try
            {
                if (_operatorService.CurrentOperator == null)
                {
                    _logger.LogWarning("Attempt to cancel tasks without operator authentication.");
                    await Dispatcher.InvokeAsync(() => MessageBox.Show("Авторизуйтесь, считав карту!"));
                    return;
                }

                var selectedTasks = await Dispatcher.InvokeAsync(() => dgTasks.SelectedItems.Cast<TaskViewModel>().ToList());
                if (!selectedTasks.Any())
                {
                    _logger.LogWarning("No tasks selected in dgTasks when attempting to cancel.");
                    await Dispatcher.InvokeAsync(() => MessageBox.Show("Выберите хотя бы одну задачу в таблице!"));
                    return;
                }

                using (var db = _dbFactory.CreateDbContext())
                {
                    try
                    {
                        var operatorId = await _operatorService.GetOperatorIdAsync(_operatorService.CurrentOperator.PersonnelNumber).ConfigureAwait(false);
                        if (operatorId == null)
                        {
                            _logger.LogWarning("OperatorId not found for personnelNumber: {PersonnelNumber}", _operatorService.CurrentOperator.PersonnelNumber);
                            await Dispatcher.InvokeAsync(() => MessageBox.Show("Не удалось определить ID оператора."));
                            return;
                        }

                        var now = DateTime.Now;
                        var today = now.Date;
                        DateTime shiftStart = now.Hour >= 8 && now.Hour < 20 ? today.AddHours(8) : today.AddHours(20);
                        _logger.LogInformation("ShiftStart calculated: {ShiftStart}", shiftStart);

                        using (var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false))
                        {
                            try
                            {
                                int canceledCount = 0;
                                foreach (var task in selectedTasks)
                                {
                                    var assignmentId = task.Id;
                                    _logger.LogInformation("Processing task cancellation for AssignmentId={AssignmentId}", assignmentId);

                                    var existingExecution = await db.TOExecutions
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(e => e.AssignmentId == assignmentId && e.DueDateTime == shiftStart)
                                        .ConfigureAwait(false);
                                    if (existingExecution != null)
                                    {
                                        _logger.LogWarning("Task already processed for AssignmentId={AssignmentId}, DueDateTime={DueDateTime}, Status={Status}",
                                            assignmentId, existingExecution.DueDateTime, existingExecution.Status);
                                        continue;
                                    }

                                    var execution = new Execution
                                    {
                                        AssignmentId = assignmentId,
                                        OperatorId = operatorId,
                                        ExecutionTime = now,
                                        Status = 2,
                                        DueDateTime = shiftStart
                                    };
                                    db.TOExecutions.Add(execution);

                                    var assignment = await db.TOWorkAssignments
                                        .FirstOrDefaultAsync(a => a.Id == assignmentId)
                                        .ConfigureAwait(false);
                                    if (assignment == null)
                                    {
                                        _logger.LogError("Assignment not found for Id: {AssignmentId}", assignmentId);
                                        continue;
                                    }

                                    assignment.LastExecTime = shiftStart;
                                    canceledCount++;
                                }

                                await db.SaveChangesAsync().ConfigureAwait(false);
                                await transaction.CommitAsync().ConfigureAwait(false);

                                var message = canceledCount == 1 ? "Задача отменена!" : $"Отменено: {canceledCount} задач(и)!";
                                await Dispatcher.InvokeAsync(() => MessageBox.Show(message));
                                _logger.LogInformation("Tasks canceled: Count={Count}, OperatorId={OperatorId}, ShiftStart={ShiftStart}", canceledCount, operatorId, shiftStart);

                                _isLoadingTasks = false;
                                await LoadTasksAsync();
                            }
                            catch (DbUpdateException ex)
                            {
                                await transaction.RollbackAsync().ConfigureAwait(false);
                                _logger.LogError(ex, "Error canceling tasks");
                                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Ошибка при сохранении: {ex.InnerException?.Message ?? ex.Message}"));
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync().ConfigureAwait(false);
                                _logger.LogError(ex, "Unexpected error canceling tasks");
                                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Непредвиденная ошибка: {ex.Message}"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error in BtnCancelTask_Click");
                        await Dispatcher.InvokeAsync(() => MessageBox.Show($"Ошибка: {ex.Message}"));
                    }
                }
            }
            finally
            {
                await Dispatcher.InvokeAsync(() => btnCancelTask.IsEnabled = true);
                _logger.LogInformation("BtnCancelTask_Click completed, ThreadId={ThreadId}", Thread.CurrentThread.ManagedThreadId);
            }
        }

        private async void BtnOpenAdmin_Click(object sender, RoutedEventArgs e)
        {
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