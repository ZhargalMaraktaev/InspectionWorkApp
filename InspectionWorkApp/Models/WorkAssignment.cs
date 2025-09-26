using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InspectionWorkApp.Models
{
    public class WorkAssignment
    {
        public int Id { get; set; }
        public int WorkId { get; set; }
        public Work Work { get; set; }
        public int FreqId { get; set; }
        public WorkFrequency Freq { get; set; }
        public int RoleId { get; set; }
        public Role Role { get; set; }
        public int WorkTypeId { get; set; }
        public TOWorkTypes WorkType { get; set; }
        public int SectorId { get; set; }
        public Sector Sector { get; set; }
        public bool IsCanceled { get; set; }
        public DateTime? LastExecTime { get; set; }
    }
}
