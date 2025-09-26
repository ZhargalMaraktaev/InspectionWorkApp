using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InspectionWorkApp.Models; // Для моделей
using InspectionWorkApp.Services; // Для OperatorService


namespace InspectionWorkApp.Controllers
{
    // Класс для управления COM-считывателем
    public class COMController : IDisposable
    {
        // Событие, которое вызывается при изменении состояния считывателя
        public event EventHandler<COMEventArgs.ReadingDataEventArgs>? StateChanged;

        // Параметры подключения к COM-порту
        private COMControllerParamsModel ComControllerParamsModel { get; }

        // Время между попытками переподключения
        public int TimeToReconnect { get; set; } = 500;

        // Свойство, активна ли сейчас операция чтения
        public bool IsReading
        {
            get => isReading;
            set
            {
                if (isReading == value)
                    return;

                if (value)
                {
                    // Если не удалось инициализировать порт — выдаём ошибку
                    if (!InitializeSerialPort())
                    {
                        isReading = false;

                        CleanupSerialPort();

                        messageQueue.Clear();
                        currentBuffer.Clear();

                        StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(null, COMControllerParamsModel.COMStates.None)
                        {
                            ErrorCode = (int)COMControllerParamsModel.ErrorCodes.ConnectionError,
                            ErrorText = "Ошибка подключения к считывателю."
                        });
                    }
                    else
                    {
                        isReading = value;
                        cts = new CancellationTokenSource();
                        // Запускаем фоновую задачу для чтения данных
                        processQueueTask = Task.Run(() => ProcessQueue(cts.Token), cts.Token);
                    }
                }
                else
                {
                    isReading = value;
                    cts?.Cancel();
                    CleanupSerialPort();
                    messageQueue.Clear();
                    currentBuffer.Clear();
                }
            }
        }

        private bool isReading = false;
        private SerialPort? serialPort;
        private ConcurrentQueue<string> messageQueue;
        private StringBuilder currentBuffer;
        private CancellationTokenSource? cts;
        private Task? processQueueTask;

        // Конструктор: принимает модель параметров порта
        public COMController(COMControllerParamsModel comControllerParamsModel)
        {
            this.ComControllerParamsModel = comControllerParamsModel;
            this.messageQueue = new ConcurrentQueue<string>();
            this.currentBuffer = new StringBuilder();
        }

        // Обработчик входящих данных с порта
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (serialPort == null || !serialPort.IsOpen)
                    return;

                string incoming = serialPort.ReadExisting();

                lock (currentBuffer)
                {
                    currentBuffer.Append(incoming);
                    string buffer = currentBuffer.ToString();
                    int newlineIndex;

                    // Делим буфер по строкам
                    while ((newlineIndex = buffer.IndexOf('\n')) >= 0)
                    {
                        string line = buffer.Substring(0, newlineIndex).Trim('\r', '\n');
                        messageQueue.Enqueue(line);
                        buffer = buffer.Substring(newlineIndex + 1);
                    }

                    currentBuffer.Clear();
                    currentBuffer.Append(buffer);
                }
            }
            catch (Exception ex)
            {
                StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(null, COMControllerParamsModel.COMStates.None)
                {
                    ErrorCode = (int)COMControllerParamsModel.ErrorCodes.SpecificError,
                    ErrorText = $"Ошибка считывания данных.\n{ex.Message}"
                });
            }
        }

        // Фоновая обработка данных из очереди
        private async Task ProcessQueue(CancellationToken token)
        {
            Regex regex = new Regex(@"\d{1,},\d+"); // шаблон: числа с запятой

            string? detectedCardIdStr = null;
            bool thereWasAConnectionError = false;

            try
            {
                while (isReading && !token.IsCancellationRequested)
                {
                    // Переподключение, если порт отвалился
                    while (isReading && (serialPort == null || !serialPort.IsOpen))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            thereWasAConnectionError = true;
                            StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(null, COMControllerParamsModel.COMStates.None)
                            {
                                ErrorCode = (int)COMControllerParamsModel.ErrorCodes.ReadingError,
                                ErrorText = "Ошибка считывания данных. Возможно, считыватель был отключен."
                            });

                            CleanupSerialPort();

                            if (!InitializeSerialPort())
                                await Task.Delay(TimeToReconnect, token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            await Task.Delay(TimeToReconnect, token);
                        }
                    }

                    if (!isReading)
                        break;

                    // Сообщаем, что подключение восстановлено
                    if (thereWasAConnectionError)
                    {
                        thereWasAConnectionError = false;
                        StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(detectedCardIdStr, COMControllerParamsModel.COMStates.ReaderConnecting)
                        {
                            ErrorCode = (int)COMControllerParamsModel.ErrorCodes.ReaderConnecting
                        });
                    }

                    // Чтение данных
                    if (messageQueue.TryDequeue(out var serialPortData))
                    {
                        string? cardIdStr = regex.Match(serialPortData).Value;

                        if (cardIdStr.Length > 0)
                        {
                            StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(cardIdStr, COMControllerParamsModel.COMStates.Detected)
                            {
                                ErrorCode = (int)COMControllerParamsModel.ErrorCodes.ReadingSuccessful
                            });

                            detectedCardIdStr = cardIdStr;
                        }
                        else
                        {
                            StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(detectedCardIdStr, COMControllerParamsModel.COMStates.Removed)
                            {
                                ErrorCode = (int)COMControllerParamsModel.ErrorCodes.ReadingSuccessful
                            });

                            detectedCardIdStr = null;
                        }
                    }

                    await Task.Delay(50, token); // Предотвращаем перегрузку CPU
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение при отмене
            }
            catch (Exception ex)
            {
                isReading = false;
                CleanupSerialPort();
                messageQueue.Clear();
                currentBuffer.Clear();

                StateChanged?.Invoke(this, new COMEventArgs.ReadingDataEventArgs(null, COMControllerParamsModel.COMStates.None)
                {
                    ErrorCode = (int)COMControllerParamsModel.ErrorCodes.UnknownError,
                    ErrorText = $"Неизвестная ошибка считывания данных: {ex.Message}"
                });
            }
        }

        // Инициализация подключения к порту
        private bool InitializeSerialPort()
        {
            try
            {
                serialPort = new SerialPort(ComControllerParamsModel.PortName, ComControllerParamsModel.BaudRate, ComControllerParamsModel.Parity, ComControllerParamsModel.DataBits, ComControllerParamsModel.StopBits);
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка подключения к считывателю: {ex.Message}");
                return false;
            }

            return serialPort != null && serialPort.IsOpen;
        }

        // Очистка и отключение COM-порта
        public void CleanupSerialPort()
        {
            try
            {
                if (serialPort != null)
                {
                    serialPort.DataReceived -= SerialPort_DataReceived;

                    if (serialPort.IsOpen)
                        serialPort.Close();

                    serialPort.Dispose();
                    serialPort = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при очистке COM-порта: {ex.Message}");
            }
        }

        // Реализация IDisposable
        public void Dispose()
        {
            try
            {
                IsReading = false; // Останавливаем чтение
                cts?.Cancel();
                if (processQueueTask != null)
                {
                    processQueueTask.Wait(1000); // Ожидаем завершения задачи до 1 секунды
                }
                CleanupSerialPort();
                cts?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при освобождении COMController: {ex.Message}");
            }
        }
    }
}