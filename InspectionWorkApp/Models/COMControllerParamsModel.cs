using System.IO.Ports;

namespace InspectionWorkApp.Models
{
    public class COMControllerParamsModel
    {
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }

        public enum COMStates
        {
            None = 0,
            Detected = 1,
            Removed = 2,
            ReaderConnecting = 3
        }

        public enum ErrorCodes
        {
            UnknownError = -1,
            SpecificError = 0,
            ConnectionError = 1,
            ReadingError = 2,
            ReadingSuccessful = 3,
            ReaderConnecting = 4
        }
    }
}