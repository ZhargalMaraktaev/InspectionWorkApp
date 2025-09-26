using InspectionWorkApp.Models; // Для COMControllerParamsModel

namespace InspectionWorkApp.Controllers
{
    public class COMEventArgs
    {
        public class ReadingDataEventArgs : EventArgs
        {
            // Код ошибки (например, подключение, чтение, и т.д.)
            public short ErrorCode { get; set; } = 0;

            // Текст ошибки
            public string? ErrorText { get; set; }

            // Прочитанный ID карты
            public string? CardId { get; }

            // Состояние считывателя (обнаружена карта, удалена и т.д.)
            public COMControllerParamsModel.COMStates State { get; } = COMControllerParamsModel.COMStates.None;

            // Конструктор инициализирует ID карты и состояние
            public ReadingDataEventArgs(string? cardId, COMControllerParamsModel.COMStates state)
            {
                this.CardId = cardId;
                this.State = state;
            }
        }
    }
}