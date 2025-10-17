using InspectionWorkApp.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace InspectionWorkApp
{
    public partial class ReportWindow : Window
    {
        private readonly YourDbContext _db;
        private readonly DateTime _defaultExecutionTime = new DateTime(1900, 1, 1);
        private List<DateTime> _availableDates; // Список доступных дат

        public ReportWindow(YourDbContext db)
        {
            InitializeComponent();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            LoadCombos();
            LoadAvailableDates(); // Загружаем доступные даты
            LoadReports();
            dpDate.DisplayDateEnd=DateTime.Today; // Ограничиваем выбор дат сегодняшним днём и ранее
        }

        private void LoadCombos()
        {
            cmbSector.ItemsSource = _db.dic_Sector.ToList();
            cmbShift.SelectedIndex = 0; // "Все" по умолчанию
        }

        private void LoadAvailableDates()
        {
            // Загружаем уникальные даты из DueDateTime (только дата, без времени)
            _availableDates = _db.TOExecutions
                .Where(e => e.DueDateTime.HasValue)
                .Select(e => e.DueDateTime.Value.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            // Если дат нет, отключаем DatePicker
            if (!_availableDates.Any())
            {
                dpDate.IsEnabled = false;
                txtDateError.Text = "Нет данных для отображения.";
                txtDateError.Visibility = Visibility.Visible;
            }
            else
            {
                dpDate.IsEnabled = true;
                txtDateError.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadReports(DateTime? selectedDate = null, int? sectorId = null, string shiftType = "All")
        {
            var query = _db.TOExecutions
                .Include(e => e.Assignment).ThenInclude(a => a.Work)
                .Include(e => e.Assignment).ThenInclude(a => a.Sector)
                .Include(e => e.Assignment).ThenInclude(a => a.WorkType)
                .Include(e => e.Operator)
                .AsQueryable();

            if (selectedDate.HasValue)
            {
                var startDate = selectedDate.Value.Date;
                var endDate = startDate.AddDays(1);
                query = query.Where(e => e.DueDateTime >= startDate && e.DueDateTime < endDate);
            }

            if (sectorId.HasValue)
            {
                query = query.Where(e => e.Assignment.SectorId == sectorId.Value);
            }

            if (shiftType != "All")
            {
                var isDayShift = shiftType == "Day";
                query = query.Where(e => e.DueDateTime.HasValue && (e.DueDateTime.Value.Hour == 8) == isDayShift);
            }

            var reports = query.Select(e => new ReportViewModel
            {
                Id = e.Id,
                WorkName = e.Assignment.Work.WorkName,
                SectorName = e.Assignment.Sector.SectorName,
                ShiftType = e.DueDateTime.HasValue ? (e.DueDateTime.Value.Hour == 8 ? "Дневная" : "Ночная") : "Не указано",
                Status = e.Status,
                StatusName = e.Status == 1 ? "Выполнена" : e.Status == 2 ? "Не выполнена" : "Неизвестно",
                ExecutionTime = e.ExecutionTime,
                ExecutionTimeDisplay = e.ExecutionTime == _defaultExecutionTime ? "Не выполнено" : e.ExecutionTime.ToString("dd.MM.yyyy HH:mm"),
                OperatorName = e.Operator != null ? e.Operator.FIO : "Не назначен",
                Comment = e.Comment
            }).ToList();

            dgReports.ItemsSource = reports;

            // Показываем сообщение, если нет данных
            if (!reports.Any() && selectedDate.HasValue)
            {
                txtDateError.Text = "Нет данных для выбранной даты.";
                txtDateError.Visibility = Visibility.Visible;
            }
            else
            {
                txtDateError.Visibility = Visibility.Collapsed;
            }
        }

        private void DpDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dpDate.SelectedDate.HasValue)
            {
                var selectedDate = dpDate.SelectedDate.Value.Date;
                if (!_availableDates.Contains(selectedDate))
                {
                    // Если выбранная дата недоступна, сбрасываем выбор и показываем сообщение
                    dpDate.SelectedDate = null;
                    txtDateError.Text = "Для выбранной даты нет данных.";
                    txtDateError.Visibility = Visibility.Visible;
                    LoadReports(); // Загружаем все данные
                }
                else
                {
                    // Если дата валидна, обновляем отчёт
                    txtDateError.Visibility = Visibility.Collapsed;
                    int? sectorId = (cmbSector.SelectedItem as Sector)?.Id;
                    string shiftType = (cmbShift.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "All";
                    LoadReports(selectedDate, sectorId, shiftType);
                }
            }
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            DateTime? selectedDate = dpDate.SelectedDate;
            int? sectorId = (cmbSector.SelectedItem as Sector)?.Id;
            string shiftType = (cmbShift.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "All";
            LoadReports(selectedDate, sectorId, shiftType);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            dpDate.SelectedDate = null;
            cmbSector.SelectedIndex = -1;
            cmbShift.SelectedIndex = 0;
            txtDateError.Visibility = Visibility.Collapsed;
            LoadReports();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class ReportViewModel
    {
        public int Id { get; set; }
        public string WorkName { get; set; }
        public string SectorName { get; set; }
        public string ShiftType { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public DateTime ExecutionTime { get; set; }
        public string ExecutionTimeDisplay { get; set; }
        public string OperatorName { get; set; }
        public string Comment { get; set; }
    }
}