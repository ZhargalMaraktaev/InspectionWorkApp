using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InspectionWorkApp.Models
{
    public class TOFailureReason
    {
        public int Id { get; set; }
        public string ReasonText { get; set; }
        public bool IsActive { get; set; }
    }
}
