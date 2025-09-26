using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InspectionWorkApp.Models
{
    public class Skud
    {
        public int Id { get; set; }
        public string IdCard { get; set; }
        public string TabNumber { get; set; }
        public string FIO { get; set; }
        public string Department { get; set; }
        public string EmployName { get; set; }
        public int? RoleId { get; set; }
        public Role Role { get; set; }
        public int? StatId { get; set; }
        public int? TORoleId { get; set; }

    }
}
