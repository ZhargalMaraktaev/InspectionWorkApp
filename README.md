Техосмотр (InspectionWorkApp)
Что это? Приложение для технического обслуживания станков. Позволяет операторам и слесарям отмечать выполненные или не выполненные работы по техническому обслуживанию станков. Виды работ, которые выполняются в этом приложении разбиваются по видам:
1) Контрольно-осмотровые работы.
2) Регламентные работы.
3) Ремонтные работы.
Где находится? Компьютеры на станках участка НОТ, УРЗиМ (Опресовка, Муфтонаворот, МО 5, МО 6, Муфтовый 1,2,3,4) Размножается при помощи ClickOnce.
Кому надо? Мастера НОТ, УРЗиМ.
Как работает?
	Интерфейс представляет с собой окно со списком работ для выполнения для конкретного участка. Имена компьютеров привязаны к конкретным участкам так что открыть работы, которые относятся к другому участку не получиться, но при этом через БД можно назначить на 1 компьютер сколько угодно участков и работы для него будут отображаться.
 
Рис.1 Интерфейс после авторизации.
Изначально интерфейс с работами перекрыт пустым окном с сообщением «Вставьте Пропуск». 
Виды ролей, существующих в программе:
1.	Оператор.
2.	Слесарь.
3.	Администратор.
Оператор или слесарь должен вставить свой пропуск в считыватель, что авторизоваться в приложении, программа автоматически определит роль сотрудника, приложившего пропуск и выдаст список с работами к выполнению. Далее исполнитель выполняет или не выполняет работу, указанную в списке работ по техническому обслуживанию станка, и отмечает это соответствующей кнопкой «выполнена» или «не выполнена». В случае если по какой-то причине работа не была выполнена сотрудник указывает причину наиболее подходящую из предложенных во всплывающем окне. В программе существует третья роль это администратор, в случае если пропуск приложит человек с должностью которая содержит формулировки:
1. Начальник.
2. Мастер.
3. Инженер.
4. Заместитель.
5. Директор.
То данному сотруднику будет присвоена роль «Администратор». Администратор имеет доступ к админ-панели, через которую он может создавать работы, создавать назначения по этим работам, отменять назначения, удалять работы, просматривать выполненные работы. Администратор не может отмечать выполненные или не выполненные работы.
Язык: C#  
Фреймворк: WPF (.NET Framework / .NET 9)  
База данных: есть подключение (Entity Framework, см. DBContext.cs)  
Цель приложения
- Ведение осмотров / технического обслуживания станков
- Регистрация отказов выполнения работ по существующим причинам
- Указание причин отказов выполнения работ (FailureReasonWindow)
- Просмотр отмеченных работ (ReportWindow)
- Администрирование пользователей / данных (AdminWindow)
- В случае если сотрудника нет в БД вытягивать данные из 1c (employee_data.xml)
Текущая структура проекта
InspectionWorkApp/
├── Controllers/                ← контроллеры
├── Converters/                 ← value converters для WPF binding
├── Interfaces/                 ← интерфейсы сервисов / репозиториев
├── Models/                     ← сущности / модели данных
├── Services/                   ← бизнес-логика, сервисы
│
├── AdminWindow.xaml            ← админ-панель
├── AdminWindow.xaml.cs             ← код админ панели
├── FailureReasonWindow.xaml    ← окно причин отказа / неисправности
├── FailureReasonWindow.xaml.cs     ← код окна причин отказа / неисправности
├── MainWindow.xaml             ← главное окно
├── MainWindow.xaml.cs         ← код главного окна
├── ReportWindow.xaml           ← окно отмеченных работ
├── ReportWindow.xaml.cs       ← код окна отмеченных работ
│
├── App.xaml                   
├── App.xaml.cs		← точка входа приложения
├── App.config		← конфигурация приложения
├── appsettings.json            ← настройки и строки подключения к БД.
├── DBContext.cs                ← контекст БД (Entity Framework)
├── DataInitializer.cs          ← инициализация данных / seed
├── DatabaseSettings.cs         ← целевая БД
├── GenerateAssignmentsJob.cs   ← генерация назначений background job, старая логика которая неиспользуется)
│
├── employee_data.xml           ← шаблон xml запроса в 1c.
├── InspectionWorkApp.csproj
├── InspectionWorkApp.csproj.user
├── AssemblyInfo.cs
└── .gitignore
Архитектура
Приложение следует MVVM-подобной структуре с разделением на слои:
•	Модели (Models): POCO-классы для сущностей БД и вспомогательных данных (e.g., Employee1CModel для 1C-ответов).
•	Представления (Views): WPF-окна и XAML (MainWindow, AdminWindow и т.д.).
•	ViewModels: TaskViewModel для биндинга данных в UI (INotifyPropertyChanged для обновлений).
•	Контроллеры (Controllers): Обработка внешних устройств и API (COMController, Controller1C).
•	Сервисы (Services): Бизнес-логика (OperatorService, DataAccessLayer).
•	Репозитории: Интерфейсы как IEmployeeRepository для абстракции доступа к данным.
•	Планировщик: Quartz.NET с SimpleJobFactory для интеграции с DI.
•	Конфигурация: appsettings.json (строки подключения, TargetDatabase как "Pilot") и localsettings.json в AppData для локальных настроек.
•	Логирование: Serilog с файл-логами в AppData\InspectionWorkApp\Logs.
•	DI: Microsoft.Extensions.DependencyInjection в App.xaml.cs для регистрации сервисов (e.g., YourDbContext, OperatorService).
Приложение использует асинхронные паттерны (async/await, Task.Run), семафоры (AsyncLock для thread-safety) и события (e.g., OnOperatorChanged) для реактивности.
База данных
Используется MS SQL Server (подключение в appsettings.json). Контекст: YourDbContext (DbContext с OnModelCreating для конфигурации).
Ключевые таблицы (dbo-схема):
•	dic_Sector: Сектора (Id, SectorName, SiteTag).
•	TORoles: Роли (Id, RoleName). FK: В dic_SKUD.
•	dic_SKUD: Сотрудники (Id, idCard, TabNumber, FIO, Department, EmployName, TORoleId). FK: TORoles.
•	TOWorks: Работы (Id, WorkName).
•	TOWorkTypes: Типы работ (Id, WorkType, e.g., "Обычная", "КО").
•	TOWorkFrequencies: Частоты (Id, Type, Frequency, IntervalDay, IntervalHour).
•	TOStatuses: Статусы (Id, StatusName, e.g., "Выполнена").
•	TOWorkAssignments: Назначения (Id, WorkId, FreqId, RoleId, WorkTypeId, SectorId, IsCanceled, LastExecTime). Индексы: (RoleId, SectorId, IsCanceled); Уникальный: (WorkId, SectorId). FK: TOWorks, TOWorkFrequencies, TORoles, TOWorkTypes, dic_Sector.
•	TOExecutions: История выполнения (Id, AssignmentId, OperatorId, ExecutionTime (default 1900-01-01), Status, DueDateTime, Comment). Индексы: (AssignmentId, DueDateTime). FK: TOWorkAssignments, dic_SKUD, TOStatuses.
•	dic_PCNameSector: Маппинг ПК с участками и COM портами (Id, NamePC, Sector, CardReaderCOMPort). FK: dic_Sector.
•	TOFailureReasons: Причины невыполнения (Id, ReasonText, IsActive).
Инициализация: DataInitializer добавляет дефолтные данные (роли, сектора и т.д.).
Основные компоненты и их логика
1. Модели (Models)
Модели определяют структуру данных. Все наследуют базовые свойства (Id), некоторые имеют навигационные свойства для EF.
•	Sector: Представляет участок. Логика: Группировка заданий; используется в фильтрах (e.g., LoadTasksAsync в MainWindow).
•	Role: Роль (e.g., Id=1 "Оператор", Id=2 "Администратор", Id=4 "Инженер"). Логика: Контроль доступа (e.g., в MainWindow проверка _currentRoleId для кнопок).
•	Work: Базовая работа. Логика: Справочник для назначений.
•	WorkAssignment: Ключевой класс для планирования. Логика: IsCanceled флаг отмены; LastExecTime для расчета следующего срока (e.g., в GenerateTasksJob: if (now - LastExecTime > IntervalDay)).
•	Execution: История выполнения. Логика: Status (1=Выполнено, 2=Не выполнено); DueDateTime для сроков (08:00 день, 20:00 ночь); Comment для причин (из TOFailureReasons). Default ExecutionTime=1900-01-01 для необработанных.
•	WorkFrequency: Определяет расписание. Логика: IntervalDay/Hour для периодичности (e.g., ежедневно: IntervalDay=1; еженедельно:7).
•	TOStatuses/TOWorkTypes: Простые справочники.
•	Skud: Данные сотрудника. Логика: TORoleId маппится автоматически по EmployName (DataAccessLayer: if Contains("слесарь") → 2).
•	TOFailureReason: Причины. Логика: Only IsActive=true отображаются в FailureReasonWindow.
•	PCNameSector: Логика: В App.xaml.cs по Environment.MachineName находит Sector и COMPort.
•	TaskViewModel: Для UI. Логика: IsUnprocessed для фильтров; OnPropertyChanged для биндинга; Логгирует изменения Comment.
•	Employee1CModel: Для 1C. Логика: ErrorCodes для обработки (e.g., EmployeeNotFound=1).
•	COMControllerParamsModel: Параметры порта. Логика: States (Detected=1) и ErrorCodes.
2. Контроллеры (Controllers)
•	COMController: Чтение карт. Логика:
o	Инициализация: SerialPort с BaudRate и т.д. (из PCNameSector.CardReaderCOMPort).
o	DataReceived: Парсит буфер (Regex для ID, e.g., удаляет префиксы).
o	Очередь: ConcurrentQueue для сообщений, ProcessQueue async с debounce (TimeToReconnect=500ms).
o	События: StateChanged (Detected с CardId; Removed; Errors как ConnectionError=1).
o	Dispose: Очистка порта, отмена CTS.
•	Controller1C: API 1C. Логика:
o	GetResp1CSKUD: Читает employee_data.xml, заменяет CardNumber, отправляет SOAP на URL (Basic Auth).
o	ParseSoapResponse: XmlDocument → JSON → Employee1CModel; Обрабатывает ошибки.
3. Сервисы (Services)
•	OperatorService: Авторизация. Логика:
o	Подписка на COMController.StateChanged.
o	При Detected: GetEmployeeAsync (БД или 1C via FetchAndSaveFrom1C), SyncEmployeeAsync (маппинг TORoleId).
o	CurrentOperator: Вызывает OnOperatorChanged; Null при ошибках.
•	DataAccessLayer (IEmployeeRepository): Доступ к dic_SKUD. Логика:
o	GetEmployeeAsync: SELECT по idCard или TabNumber.
o	SaveEmployeeAsync: INSERT с маппингом TORoleId (по EmployName.ToLower()).
o	SyncEmployeeAsync: UPDATE TORoleId.
o	Table(): Динамическое имя таблицы (e.g., Pilot.dbo.dic_SKUD).
•	DatabaseSettings: Конфиг для TargetDatabase.
4. UI-Компоненты (Windows)
UI-Компоненты (Windows)
MainWindow: Основное окно для операторов
MainWindow — центральный интерфейс приложения, отображающий список задач (ТО и КО работ) для текущей роли, сектора и смены. Оно интегрируется с COMController для чтения карт, OperatorService для авторизации, и YourDbContext для доступа к БД. Логика построена на асинхронных методах для избежания блокировки UI, с использованием Dispatcher для обновлений из других потоков. Ключевые принципы:
•	Авторизация: Ожидание карты; обновление статуса оператора (текст/цвет).
•	Загрузка задач: Фильтрация по роли/сектору/смене; разделение на необработанные/просроченные.
•	Выполнение задач: Кнопки "Выполнено"/"Не выполнено"; сохранение в Execution с временем, статусом и комментарием (для невыполненных — выбор причины).
•	Обновления в реальном времени: Таймеры для перезагрузки задач (каждые 5 мин); мигание для просроченных; debounce для избежания частых обновлений.
•	Доступ: Кнопки для отчетов (ReportWindow), админ-панели (AdminWindow, если роль позволяет), выхода.
•	Thread-safety: AsyncLock (_dbLock) для concurrency при доступе к БД; _stateLock для синхронизации состояний.
•	Ошибки: Логирование через ILogger; MessageBox для пользовательских ошибок (e.g., DbUpdateException).
Ключевые поля и их роль
•	_dbFactory (IDbContextFactory<YourDbContext>): Фабрика для создания DbContext (EF Core); обеспечивает disposable контексты для каждого запроса.
•	_operatorService (OperatorService): Сервис авторизации; подписка на OnOperatorChanged для обновления UI.
•	_comController (COMController): Контроллер ридера карт; запускается в конструкторе (IsReading=true).
•	_logger (ILogger<MainWindow>): Логирование событий (e.g., "Loading tasks for role {RoleId}").
•	_dayShiftDueTime (TimeSpan.FromHours(8)) / _nightShiftDueTime (TimeSpan.FromHours(20)): Сроки для дневной/ночной смен (используются в IsShiftDue и LoadTasksAsync).
•	_koWorkTypeId (2): ID для КО-работ (специальная логика: генерируются только если не выполнено).
•	_defaultExecutionTime (new DateTime(1900,1,1)): Маркер для необработанных задач (сравнение в LoadTasksAsync и ReportViewModel).
•	_stateLock (object): Для lock в критических секциях (e.g., обновление _currentRoleId).
•	_currentRoleId / _currentSectorId (int?): Текущая роль/сектор (определяются при авторизации или запуске с параметрами).
•	_isLoadingTasks (int): Флаг загрузки (0=свободно, 1=занято); предотвращает параллельные LoadTasksAsync.
•	_tasksCollection (ObservableCollection<TaskViewModel>): Коллекция для DataGrid; биндинг к UI (автообновление при изменениях).
•	_dbLock (AsyncLock): СемaphoreSlim для асинхронной блокировки БД-доступа (LockAsync/Release).
•	_lastSelectionChange (DateTime) / _debounceInterval (TimeSpan.FromMilliseconds(500)): Debounce для обработки изменений выбора (e.g., в DataGrid_SelectionChanged, чтобы избежать спама обновлений).
Другие: _availableDates (для отчетов, но в ReportWindow); константы для статусов (1=Выполнено, 2=Не выполнено).
Конструктор и инициализация
•	public AdminWindow(YourDbContext db, OperatorService operatorService): Инициализация компонентов (InitializeComponent); проверка аргументов (throw ArgumentNullException); подписка на OnOperatorChanged (для Dispatcher.Invoke UpdateOperatorStatus); LoadCombos (сектора, смены); LoadWorks (задачи).
•	Логика инициализации:
o	Подписка: _operatorService.OnOperatorChanged += OperatorService_OnOperatorChanged (Dispatcher.Invoke для UI-обновления).
o	Начальный статус: UpdateOperatorStatus (если CurrentOperator null → "Не авторизован", красный; иначе "Авторизован: {FullName}", зеленый).
o	Загрузка: LoadCombos (cmbSector/cmbShift); LoadWorks (DataGrid с TOWorks/Assignments).
o	Дополнительно: В полном коде — LoadSectorsAsync (определение _currentSectorId по PCName); StartCOMReading (COMController.IsReading=true); InitializeSchedulerAsync (но Quartz в App.xaml.cs); LoadTasksAsync (начальная загрузка задач).
Основные методы и их логика
Методы асинхронны, где возможно, с try-catch для ошибок (логгирование + MessageBox). Используют _dbLock для БД.
•	private void OperatorService_OnOperatorChanged(object sender, EventArgs e):
o	Логика: Dispatcher.Invoke → UpdateOperatorStatus. Обновляет txtCurrentOperator (текст/цвет по CurrentOperator). Если авторизован — LoadTasksAsync для загрузки задач под ролью.
•	private void UpdateOperatorStatus():
o	Логика: проверяет _operatorService.CurrentOperator; устанавливает Text и Foreground для txtCurrentOperator. Вызывается при изменении оператора или инициализации.
•	private async Task LoadTasksAsync() (или аналог LoadWorks/LoadReports, но фокус на задачах):
o	Логика:
	Проверка: if (_isLoadingTasks == 1 || !_currentRoleId.HasValue || !_currentSectorId.HasValue) return; Interlocked.Exchange для флага.
	Блокировка: using (var releaser = await _dbLock.LockAsync()).
	Запрос: using (var db = _dbFactory.CreateDbContext()) → LINQ to TOExecutions/Assignments (Where: RoleId==_currentRoleId, SectorId==_currentSectorId, IsCanceled=false, DueDateTime в текущей смене).
	Фильтры: IsShiftDue (now >= shiftStart && now <= shiftEnd + gracePeriod?); isDue = now >= DueDateTime; isDone = ExecutionTime != _defaultExecutionTime.
	Преобразование: В TaskViewModel (WorkName, WorkType, DueDateTime, StatusName, IsUnprocessed=true если !isDone).
	UI-обновление: Dispatcher.Invoke → _tasksCollection.Clear/AddRange; Сортировка (e.g., по DueDateTime).
	Завершение: _isLoadingTasks=0; Лог: "Tasks loaded: {Count}".
	Debounce: Если от SelectionChanged — проверка (DateTime.Now - _lastSelectionChange > _debounceInterval).
•	private async Task SaveExecutionAsync(int assignmentId, int status, string comment = null):
o	Логика:
	Блокировка: await _dbLock.LockAsync().
	DbContext: using (var db = _dbFactory.CreateDbContext()).
	Поиск: var execution = await db.TOExecutions.FirstOrDefaultAsync(e => e.AssignmentId == assignmentId && e.DueDateTime == currentShift).
	Обновление/Создание: if (execution == null) new Execution; else update (ExecutionTime=Now, Status=status, OperatorId=CurrentOperator.Id, Comment=comment).
	Сохранение: await db.SaveChangesAsync(); Лог: "Execution saved: Id={Id}, Status={Status}".
	Обновление UI: await LoadTasksAsync(); CheckAllTasksCompleted (если все выполнены — уведомление?).
•	private void BtnExecuted_Click(object sender, RoutedEventArgs e):
o	Логика: получить selectedTask (из DataGrid.SelectedItem as TaskViewModel); if null return; await SaveExecutionAsync(selectedTask.Id, 1); MessageBox "Задача выполнена!".
•	private void BtnNotExecuted_Click(object sender, RoutedEventArgs e):
o	Логика: Аналог BtnExecuted, но Status=2; открывает FailureReasonWindow (ShowDialog); if DialogResult=true → comment=SelectedReason; await SaveExecutionAsync(..., 2, comment); MessageBox "Задача отмечена как невыполненная.".
•	private void BtnReport_Click(object sender, RoutedEventArgs e) / BtnAdmin_Click / BtnClose_Click:
o	Логика: new ReportWindow(_dbFactory).ShowDialog(); или new AdminWindow(_db, _operatorService).Show(); или Close() (с OnClosing).
•	protected override void OnClosing(System.ComponentModel.CancelEventArgs e):
o	Логика: Отписки (_operatorService.OnOperatorChanged -= ...); _comController.IsReading=false; _comController.Dispose(); base.OnClosing.
•	Другие вспомогательные методы:
o	IsShiftDue(DateTime due, DateTime now): Проверяет, актуальна ли задача (now >= due && !isDone).
o	HasUncompletedTasksAsync(): Запрос Any TOExecutions (Where !isDone && isDue); для уведомлений.
o	DataGrid_SelectionChanged: Debounce → _lastSelectionChange=Now; if (debounce) return; Update buttons enabled.
o	Timer_Tick (DispatcherTimer): await LoadTasksAsync(); для автообновления.
o	IsOverdue(DateTime due): now > due + gracePeriod? → Мигание UI (анимация в XAML, e.g., Storyboard).
UI-элементы и биндинг
•	DataGrid: ItemsSource=_tasksCollection; Columns для WorkName, DueDateTime (с DateTimeConverter: если ExecutionTime != default → показ ExecutionTime).
•	Текст: txtCurrentOperator (биндинг к PropertyChanged).
•	Кнопки: Enabled по selectedTask && IsUnprocessed.
•	Конвертеры: DateTimeConverter (в Converters) для форматирования дат.
Интеграция и edge-cases
•	С Quartz: Задачи генерируются ночью; MainWindow загружает актуальные.
•	Просроченные: Отдельный раздел в UI; мигание (Storyboard в XAML).
•	Конкурренция: _dbLock предотвращает race conditions (e.g., multiple SaveExecution).
•	Логи: _logger.LogInformation/Error для всех ключевых действий.
•	Тестирование: Учитывать асинхронность; mock _dbFactory для unit-тестов.
•	AdminWindow: Админ. Логика:
o	LoadWorks/LoadCombos: Запросы справочников.
o	BtnAddWork/BtnEditWork/BtnDeleteWork: CRUD для TOWorks и Assignments (RemoveRange для связанных).
o	OnOperatorChanged: Обновление статуса.
•	ReportWindow: Отчеты. Логика:
o	LoadAvailableDates: Уникальные DueDateTime.Date.
o	LoadReports: Запрос TOExecutions с фильтрами (Date, Sector, Shift); ReportViewModel для DataGrid (ExecutionTimeDisplay).
•	FailureReasonWindow: Выбор причины. Логика: LoadReasonsAsync (IsActive=true); Кнопки для ReasonText.
5. Планирование задач (Quartz.NET)
•	App.xaml.cs: Регистрация Scheduler (StdSchedulerFactory, SimpleJobFactory).
•	GenerateTasksJob: IJob. Логика:
o	Execute: GenerateTasksAsync для всех Assignments (по Freq: if overdue → new Execution с DueDateTime).
o	Смены: Day (08:00), Night (20:00); KO (WorkTypeId=2) только если не выполнено.
o	Обновление LastExecTime.
Логика работы приложения
1.	Запуск (App.xaml.cs): Чтение параметров из БД (ReadStartupParametersAsync: SELECT/UPDATE в ExchangeTable); Определение Sector/COMPort по PCName; Запуск Quartz (ежедневно 00:00 GenerateTasksJob); Открытие MainWindow с ролью/картой.
2.	Авторизация: COMController → CardId → OperatorService → Employee from БД/1C → CurrentOperator.
3.	Задачи: В MainWindow: LoadTasks (unprocessed/prosroch); Выполнение → SaveExecution (Status, Comment).
4.	Администрирование: В AdminWindow: CRUD работ/назначений.
5.	Просмотр отмеченных работ: В ReportWindow: Фильтры → DataGrid.
6.	Ошибки: Логи в файлы; UI: MessageBox (e.g., DbUpdateException).
7.	Выход: OnClosing: Отписки, Dispose.

