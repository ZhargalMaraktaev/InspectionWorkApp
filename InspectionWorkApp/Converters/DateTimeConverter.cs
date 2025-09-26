using InspectionWorkApp.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace InspectionWorkApp.Converters
{
    public class DateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TaskViewModel task)
            {
                // Если задача обработана (ExecutionTime != null), показываем ExecutionTime
                if (task.ExecutionTime.HasValue && task.ExecutionTime != new DateTime(1900, 1, 1))
                {
                    return task.ExecutionTime.Value.ToString("dd.MM.yyyy HH:mm", culture);
                }
                // Иначе показываем DueDateTime
                return task.DueDateTime.ToString("dd.MM.yyyy HH:mm", culture);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}