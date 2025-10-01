using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace InspectionWorkApp.Models
{
    public class TaskViewModel : INotifyPropertyChanged
    {
        private int _id;
        private string _workName;
        private string _workType;
        private DateTime _dueDateTime;
        private string _statusName;
        private DateTime? _executionTime;
        private bool _isUnprocessed;
        private string _comment;
        private readonly ILogger _logger; // Предполагается, что у вас есть ILogger

        public TaskViewModel(ILogger<TaskViewModel> logger)
        {
            _logger = logger;
        }
        public int Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public string WorkName
        {
            get => _workName;
            set
            {
                _workName = value;
                OnPropertyChanged(nameof(WorkName));
            }
        }

        public string WorkType
        {
            get => _workType;
            set
            {
                _workType = value;
                OnPropertyChanged(nameof(WorkType));
            }
        }

        public DateTime DueDateTime
        {
            get => _dueDateTime;
            set
            {
                _dueDateTime = value;
                OnPropertyChanged(nameof(DueDateTime));
            }
        }

        public string StatusName
        {
            get => _statusName;
            set
            {
                _statusName = value;
                OnPropertyChanged(nameof(StatusName));
            }
        }

        public DateTime? ExecutionTime
        {
            get => _executionTime;
            set
            {
                _executionTime = value;
                OnPropertyChanged(nameof(ExecutionTime));
            }
        }

        public bool IsUnprocessed
        {
            get => _isUnprocessed;
            set
            {
                _isUnprocessed = value;
                OnPropertyChanged(nameof(IsUnprocessed));
            }
        }

        public string Comment
        {
            get => _comment;
            set
            {
                _comment = value;
                _logger.LogInformation("TaskViewModel Comment updated: AssignmentId={AssignmentId}, Comment={Comment}", Id, value ?? "null");
                OnPropertyChanged(nameof(Comment));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}