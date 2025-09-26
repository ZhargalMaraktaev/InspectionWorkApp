using InspectionWorkApp.Models;
using InspectionWorkApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InspectionWorkApp
{
    public partial class AdminWindow : Window
    {
        private readonly YourDbContext _db;
        private readonly OperatorService _operatorService;
        private readonly int _koWorkTypeId = 2; // Id для КО работ
        private readonly DateTime _defaultExecutionTime = new DateTime(1900, 1, 1); // Безопасное значение для DATETIME

        public AdminWindow(YourDbContext db, OperatorService operatorService)
        {
            InitializeComponent();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _operatorService = operatorService ?? throw new ArgumentNullException(nameof(operatorService));
            LoadCombos();
            LoadWorks();
        }

        private void LoadCombos()
        {
            cmbFreq.ItemsSource = _db.TOWorkFrequencies.ToList();
            cmbRole.ItemsSource = _db.TORoles.ToList();
            cmbWorkType.ItemsSource = _db.TOWorkTypes.ToList();
            cmbExistingWork.ItemsSource = _db.TOWorks.ToList();
            cmbAssignFreq.ItemsSource = _db.TOWorkFrequencies.ToList();
            cmbAssignRole.ItemsSource = _db.TORoles.ToList();
            cmbAssignWorkType.ItemsSource = _db.TOWorkTypes.ToList();
            var assignments = _db.TOWorkAssignments
                .Include(a => a.Work)
                .Include(a => a.Sector)
                .Select(a => new { a.Id, DisplayName = $"{a.Work.WorkName} - {a.Sector.SectorName}" })
                .ToList();
            cmbAssignment.ItemsSource = assignments;
        }

        private void LoadWorks()
        {
            dgWorks.ItemsSource = _db.TOWorks.ToList();
        }

        private async void BtnAddWork_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            if (string.IsNullOrEmpty(txtNewWorkName.Text) ||
                cmbFreq.SelectedItem == null ||
                cmbRole.SelectedItem == null ||
                cmbWorkType.SelectedItem == null)
            {
                MessageBox.Show("Заполните все поля!");
                return;
            }

            var work = new Work
            {
                WorkName = txtNewWorkName.Text
            };
            _db.TOWorks.Add(work);
            await _db.SaveChangesAsync();

            var freq = (WorkFrequency)cmbFreq.SelectedItem;
            var role = (Role)cmbRole.SelectedItem;
            var workType = (TOWorkTypes)cmbWorkType.SelectedItem;

            var sectors = await _db.dic_Sector.ToListAsync();
            int addedAssignments = 0;

            foreach (var sector in sectors)
            {
                var existingAssignment = await _db.TOWorkAssignments
                    .FirstOrDefaultAsync(a => a.WorkId == work.Id && a.SectorId == sector.Id);
                if (existingAssignment != null)
                {
                    continue;
                }

                var assignment = new WorkAssignment
                {
                    WorkId = work.Id,
                    FreqId = freq.Id,
                    RoleId = role.Id,
                    WorkTypeId = workType.Id,
                    SectorId = sector.Id,
                    IsCanceled = false,
                    LastExecTime = null
                };
                _db.TOWorkAssignments.Add(assignment);
                await _db.SaveChangesAsync(); // Сохраняем assignment для получения Id
                addedAssignments++;

                if (workType.Id == _koWorkTypeId)
                {
                    var dueDateTime = DateTime.Today.AddDays(1).AddHours(8); // Фиксированное время 08:00 следующего дня
                    var execution = new Execution
                    {
                        AssignmentId = assignment.Id,
                        OperatorId = null,
                        ExecutionTime = _defaultExecutionTime,
                        Status = 2,
                        DueDateTime = dueDateTime
                    };
                    _db.TOExecutions.Add(execution);
                }
            }

            if (addedAssignments == 0)
            {
                MessageBox.Show("Назначения не созданы: все сектора уже имеют назначения для выбранной работы.");
                return;
            }

            try
            {
                await _db.SaveChangesAsync();
                MessageBox.Show($"Назначения созданы для {addedAssignments} секторов!");
                LoadCombos();
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show($"Ошибка при сохранении: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private async void BtnCreateAssignment_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            if (cmbExistingWork.SelectedItem == null ||
                cmbAssignFreq.SelectedItem == null ||
                cmbAssignRole.SelectedItem == null ||
                cmbAssignWorkType.SelectedItem == null)
            {
                MessageBox.Show("Заполните все поля!");
                return;
            }

            var work = (Work)cmbExistingWork.SelectedItem;
            var freq = (WorkFrequency)cmbAssignFreq.SelectedItem;
            var role = (Role)cmbAssignRole.SelectedItem;
            var workType = (TOWorkTypes)cmbAssignWorkType.SelectedItem;

            var sectors = await _db.dic_Sector.ToListAsync();
            int addedAssignments = 0;

            foreach (var sector in sectors)
            {
                var existingAssignment = await _db.TOWorkAssignments
                    .FirstOrDefaultAsync(a => a.WorkId == work.Id && a.SectorId == sector.Id);
                if (existingAssignment != null)
                {
                    continue;
                }

                var assignment = new WorkAssignment
                {
                    WorkId = work.Id,
                    FreqId = freq.Id,
                    RoleId = role.Id,
                    WorkTypeId = workType.Id,
                    SectorId = sector.Id,
                    IsCanceled = false,
                    LastExecTime = null
                };
                _db.TOWorkAssignments.Add(assignment);
                await _db.SaveChangesAsync();
                addedAssignments++;

                if (workType.Id == _koWorkTypeId)
                {
                    var dueDateTime = DateTime.Today.AddDays(1).AddHours(8); // Фиксированное время 08:00 следующего дня
                    var execution = new Execution
                    {
                        AssignmentId = assignment.Id,
                        OperatorId = null,
                        ExecutionTime = _defaultExecutionTime,
                        Status = 2,
                        DueDateTime = dueDateTime
                    };
                    _db.TOExecutions.Add(execution);
                }
            }

            if (addedAssignments == 0)
            {
                MessageBox.Show("Назначения не созданы: все сектора уже имеют назначения для выбранной работы.");
                return;
            }

            try
            {
                await _db.SaveChangesAsync();
                MessageBox.Show($"Назначения созданы для {addedAssignments} секторов!");
                LoadCombos();
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show($"Ошибка при сохранении: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private async void BtnGenerateAssignments_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            try
            {
                var schedulerFactory = App.ServiceProvider.GetRequiredService<ISchedulerFactory>();
                var scheduler = await schedulerFactory.GetScheduler();
                await scheduler.TriggerJob(new JobKey("GenerateAssignmentsJob", "default"));
                MessageBox.Show("Генерация назначений запущена!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске генерации: {ex.Message}");
            }
        }

        private async void BtnDeleteAssignments_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            var result = MessageBox.Show("Вы уверены, что хотите удалить все назначения и связанные задачи?",
                                        "Подтверждение удаления",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var executions = await _db.TOExecutions.ToListAsync();
                _db.TOExecutions.RemoveRange(executions);
                var assignments = await _db.TOWorkAssignments.ToListAsync();
                _db.TOWorkAssignments.RemoveRange(assignments);
                await _db.SaveChangesAsync();
                MessageBox.Show("Все назначения и задачи успешно удалены!");
                LoadCombos();
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show($"Ошибка при удалении назначений: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private async void BtnCreateTask_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            if (cmbAssignment.SelectedItem == null)
            {
                MessageBox.Show("Выберите назначение!");
                return;
            }

            var assignmentId = (int)((dynamic)cmbAssignment.SelectedItem).Id;
            var assignment = await _db.TOWorkAssignments
                .Include(a => a.WorkType)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            DateTime? dueDateTime = null;
            if (!string.IsNullOrEmpty(txtDueDateTime.Text) &&
                txtDueDateTime.Text != "ДД.ММ.ГГГГ ЧЧ:ММ (опционально)" &&
                DateTime.TryParse(txtDueDateTime.Text, out var parsedDateTime))
            {
                if (parsedDateTime < new DateTime(1753, 1, 1))
                {
                    MessageBox.Show("Дата должна быть не ранее 01.01.1753!");
                    return;
                }
                dueDateTime = parsedDateTime;
            }

            var newExecution = new Execution
            {
                AssignmentId = assignmentId,
                OperatorId = null,
                ExecutionTime = _defaultExecutionTime,
                Status = 2,
                DueDateTime = dueDateTime
            };
            _db.TOExecutions.Add(newExecution);

            try
            {
                await _db.SaveChangesAsync();
                MessageBox.Show("Задача создана!");
                LoadCombos();
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show($"Ошибка при сохранении задачи: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private async void BtnDeleteWork_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            if ((sender as Button)?.DataContext is Work selectedWork)
            {
                try
                {
                    _db.TOWorks.Remove(selectedWork);
                    await _db.SaveChangesAsync();
                    MessageBox.Show("Работа удалена вместе с назначениями!");
                    LoadWorks();
                    LoadCombos();
                }
                catch (DbUpdateException ex)
                {
                    MessageBox.Show($"Ошибка при удалении работы: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}