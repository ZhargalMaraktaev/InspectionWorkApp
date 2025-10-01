using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InspectionWorkApp.Models
{
    public class Execution
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public WorkAssignment Assignment { get; set; }
        public int? OperatorId { get; set; }
        public Skud Operator { get; set; }
        public DateTime ExecutionTime { get; set; }
        public int Status { get; set; }
        public TOStatuses status { get; set; }
        public DateTime? DueDateTime { get; set; }
        public string Comment { get; set; } // Новое поле для комментария
    }
}
