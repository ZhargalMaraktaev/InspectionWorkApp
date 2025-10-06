using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InspectionWorkApp.Models
{
    public class Employee1CModel
    {
        public enum ErrorCodes
        {
            UnknownError = -1,
            SpecificError = 0,
            EmployeeNotFound = 1,
            ConnectionError = 2,
            ReadingError = 3,
            ReadingSuccessful = 4,
            ReaderConnecting = 5
        }

        public string CardNumber { get; set; }
        public string PersonnelNumber { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
        public string Position { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorText { get; set; }
        public int? TORoleId { get; set; } // Добавлено поле для TORoleId
    }
}
