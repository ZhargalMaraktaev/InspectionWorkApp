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
        public AdminWindow(YourDbContext db, OperatorService operatorService)
        {
            InitializeComponent();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _operatorService = operatorService ?? throw new ArgumentNullException(nameof(operatorService));

            // Подписка на событие изменения оператора
            _operatorService.OnOperatorChanged += OperatorService_OnOperatorChanged;

            // Установка начального состояния txtCurrentOperator
            UpdateOperatorStatus();

            LoadCombos();
            LoadWorks();
        }

        private void OperatorService_OnOperatorChanged(object sender, EventArgs e)
        {
            // Обновление статуса оператора при изменении
            Dispatcher.Invoke(() =>
            {
                UpdateOperatorStatus();
            });
        }

        private void UpdateOperatorStatus()
        {
            if (_operatorService.CurrentOperator != null)
            {
                txtCurrentOperator.Text = $"Авторизован: {_operatorService.CurrentOperator.FullName}";
                txtCurrentOperator.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                txtCurrentOperator.Text = "Не авторизован";
                txtCurrentOperator.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Отписка от события для предотвращения утечек памяти
            _operatorService.OnOperatorChanged -= OperatorService_OnOperatorChanged;
            base.OnClosing(e);
        }

        private void LoadCombos()
        {
            //cmbFreq.ItemsSource = _db.TOWorkFrequencies.ToList();
            //cmbRole.ItemsSource = _db.TORoles.ToList();
            //cmbWorkType.ItemsSource = _db.TOWorkTypes.ToList();
            cmbExistingWork.ItemsSource = _db.TOWorks.ToList();

            // Загрузка секторов для комбобокса
            cmbSector.ItemsSource = _db.dic_Sector.ToList();

            cmbAssignFreq.ItemsSource = _db.TOWorkFrequencies.ToList();
            cmbAssignRole.ItemsSource = _db.TORoles.ToList();
            cmbAssignWorkType.ItemsSource = _db.TOWorkTypes.ToList();
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

            if (string.IsNullOrEmpty(txtNewWorkName.Text))
            {
                MessageBox.Show("Заполните поле!");
                return;
            }

            var work = new Work
            {
                WorkName = txtNewWorkName.Text
            };
            _db.TOWorks.Add(work);

            try
            {
                await _db.SaveChangesAsync();
                MessageBox.Show("Работа успешно создана!");
                LoadCombos();
                LoadWorks();
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
                cmbSector.SelectedItem == null ||
                cmbAssignFreq.SelectedItem == null ||
                cmbAssignRole.SelectedItem == null ||
                cmbAssignWorkType.SelectedItem == null)
            {
                MessageBox.Show("Заполните все поля!");
                return;
            }

            var work = (Work)cmbExistingWork.SelectedItem;
            var sector = (Sector)cmbSector.SelectedItem;
            var freq = (WorkFrequency)cmbAssignFreq.SelectedItem;
            var role = (Role)cmbAssignRole.SelectedItem;
            var workType = (TOWorkTypes)cmbAssignWorkType.SelectedItem;

            // Проверяем, существует ли уже назначение для этой работы и сектора
            var existingAssignment = await _db.TOWorkAssignments
                .FirstOrDefaultAsync(a => a.WorkId == work.Id &&
                                        a.SectorId == sector.Id &&
                                        !a.IsCanceled);

            if (existingAssignment != null)
            {
                MessageBox.Show($"Назначение для работы '{work.WorkName}' и сектора '{sector.SectorName}' уже существует!");
                return;
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

            try
            {
                await _db.SaveChangesAsync();
                MessageBox.Show($"Назначение успешно создано для сектора '{sector.SectorName}'!");
                LoadCombos();
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show($"Ошибка при сохранении: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private async void BtnCancelAssignments_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            if ((sender as Button)?.DataContext is Work selectedWork)
            {
                var result = MessageBox.Show($"Вы уверены, что хотите отменить все назначения для работы '{selectedWork.WorkName}'?",
                                           "Подтверждение отмены",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    var assignments = await _db.TOWorkAssignments
                        .Where(a => a.WorkId == selectedWork.Id && !a.IsCanceled)
                        .ToListAsync();

                    foreach (var assignment in assignments)
                    {
                        assignment.IsCanceled = true;
                    }

                    await _db.SaveChangesAsync();
                    MessageBox.Show("Все назначения для выбранной работы успешно отменены!");
                    LoadCombos();
                }
                catch (DbUpdateException ex)
                {
                    MessageBox.Show($"Ошибка при отмене назначений: {ex.InnerException?.Message ?? ex.Message}");
                }
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

        private async void BtnDeleteWork_Click(object sender, RoutedEventArgs e)
        {
            if (_operatorService.CurrentOperator == null)
            {
                MessageBox.Show("Авторизуйтесь, считав карту!");
                return;
            }

            if ((sender as Button)?.DataContext is Work selectedWork)
            {
                var result = MessageBox.Show($"Вы уверены, что хотите удалить работу '{selectedWork.WorkName}' и все связанные с ней назначения и задачи?",
                                            "Подтверждение удаления",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    // First, delete related executions
                    var executions = await _db.TOExecutions
                        .Where(ex => _db.TOWorkAssignments
                            .Where(a => a.WorkId == selectedWork.Id)
                            .Select(a => a.Id)
                            .Contains(ex.AssignmentId))
                        .ToListAsync();
                    if (executions.Any())
                    {
                        _db.TOExecutions.RemoveRange(executions);
                    }

                    // Then, delete related assignments
                    var assignments = await _db.TOWorkAssignments
                        .Where(a => a.WorkId == selectedWork.Id)
                        .ToListAsync();
                    if (assignments.Any())
                    {
                        _db.TOWorkAssignments.RemoveRange(assignments);
                    }

                    // Finally, delete the work
                    _db.TOWorks.Remove(selectedWork);

                    await _db.SaveChangesAsync();
                    MessageBox.Show("Работа и все связанные назначения и задачи успешно удалены!");
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