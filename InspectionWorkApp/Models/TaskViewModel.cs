using System;

namespace InspectionWorkApp.Models
{
    public class TaskViewModel
    {
        public int Id { get; set; }
        public string WorkName { get; set; }
        public string SectorName { get; set; }
        public string WorkType { get; set; }
        public DateTime DueDateTime { get; set; }
        public string StatusName { get; set; }
        public DateTime? ExecutionTime { get; set; }
    }
}