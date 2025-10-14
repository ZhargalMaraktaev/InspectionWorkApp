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

        public ReportWindow(YourDbContext db)
        {
            InitializeComponent();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            LoadCombos();
            LoadReports();
        }

        private void LoadCombos()
        {
            cmbSector.ItemsSource = _db.dic_Sector.ToList();
            cmbShift.SelectedIndex = 0; // "Все" по умолчанию
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
                ExecutionTimeDisplay = e.ExecutionTime == new DateTime(1900, 1, 1) ? "Не выполнено" : e.ExecutionTime.ToString("dd.MM.yyyy HH:mm"),
                OperatorName = e.Operator != null ? e.Operator.FIO : "Не назначен",
                Comment = e.Comment
            }).ToList();

            dgReports.ItemsSource = reports;
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
        public string ExecutionTimeDisplay { get; set; } // Свойство для отображения времени
        public string OperatorName { get; set; }
        public string Comment { get; set; }
    }
}