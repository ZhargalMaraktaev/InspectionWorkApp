using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

                    foreach (var reason in reasons)
                    {
                        var button = new Button
                        {
                            Content = reason.ReasonText,
                            Margin = new Thickness(5),
                            Padding = new Thickness(10, 5, 10, 5),
                            Width = 180, // Ширина кнопки для единообразия
                            Height = 50, // Высота кнопки
                            FontSize = 14,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Tag = reason.ReasonText // Сохраняем текст причины в Tag
                        };
                        button.Click += ReasonButton_Click;
                        wrapReasons.Children.Add(button);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке причин: {ex.Message}");
            }
        }

        private void ReasonButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                SelectedReason = button.Tag as string;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}