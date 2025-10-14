using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using InspectionWorkApp.Models;

namespace InspectionWorkApp
{
    public partial class FailureReasonWindow : Window
    {
        private readonly IDbContextFactory<YourDbContext> _dbFactory;
        public string SelectedReason { get; private set; }

        public FailureReasonWindow(IDbContextFactory<YourDbContext> dbFactory)
        {
            InitializeComponent();
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            LoadReasonsAsync();
        }

        private async void LoadReasonsAsync()
        {
            try
            {
                using (var db = _dbFactory.CreateDbContext())
                {
                    var reasons = await db.TOFailureReasons
                        .Where(r => r.IsActive)
                        .ToListAsync();
                    lstReasons.ItemsSource = reasons;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке причин: {ex.Message}");
            }
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            var selectedReason = lstReasons.SelectedItem as TOFailureReason;
            if (selectedReason != null)
            {
                SelectedReason = selectedReason.ReasonText;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Выберите причину!");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

   
}