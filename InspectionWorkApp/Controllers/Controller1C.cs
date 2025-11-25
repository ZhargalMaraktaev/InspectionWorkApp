using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;
using InspectionWorkApp.Models; // Для Employee1CModel

namespace InspectionWorkApp.Controllers
{
    public class Controller1C
    {
        public async Task<Employee1CModel> GetResp1CSKUD(string cardNumber)
        {
            string xmlPattern;
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "employee_data.xml");
                xmlPattern = await File.ReadAllTextAsync(filePath);
            }
            catch (FileNotFoundException ex)
            {
                return new Employee1CModel
                {
                    ErrorCode = (int)Employee1CModel.ErrorCodes.SpecificError,
                    ErrorText = $"Файл employee_data.xml не найден по пути: {ex.FileName}"
                };
            }
            catch (IOException ex)
            {
                return new Employee1CModel
                {
                    ErrorCode = (int)Employee1CModel.ErrorCodes.SpecificError,
                    ErrorText = $"Ошибка чтения файла employee_data.xml: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new Employee1CModel
                {
                    ErrorCode = (int)Employee1CModel.ErrorCodes.UnknownError,
                    ErrorText = $"Неизвестная ошибка при чтении файла employee_data.xml: {ex.Message}"
                };
            }

            // Replace placeholder with card number
            string soapEnvelope = xmlPattern.Replace("CardNumber", cardNumber);

            string url = "http://192.168.12.25/ITPZ_ST/ru_RU/ws/emp_data";

            var handler = new HttpClientHandler
            {
                Credentials = new CredentialCache
                {
                    {
                        new Uri(url), "Basic", new NetworkCredential("obmen", "ghbrjk")
                    }
                }
            };

            using HttpClient client = new HttpClient(handler);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("SOAPAction", "\"emp_data#emp_data:export_data\"");
                request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

                HttpResponseMessage response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return new Employee1CModel
                    {
                        ErrorCode = (int)Employee1CModel.ErrorCodes.SpecificError,
                        ErrorText = $"Ошибка HTTP-запроса: {response.StatusCode} - {response.ReasonPhrase}"
                    };
                }

                string soapXml = await response.Content.ReadAsStringAsync();

                Employee1CModel? employee = ParseSoapResponse(soapXml, cardNumber);

                return employee ?? new Employee1CModel
                {
                    ErrorCode = (int)Employee1CModel.ErrorCodes.UnknownError,
                    ErrorText = "Не удалось обработать ответ от сервера 1С."
                };
            }
            catch (HttpRequestException ex)
            {
                return new Employee1CModel
                {
                    ErrorCode = (int)Employee1CModel.ErrorCodes.SpecificError,
                    ErrorText = $"Ошибка HTTP-запроса: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                return new Employee1CModel
                {
                    ErrorCode = (int)Employee1CModel.ErrorCodes.UnknownError,
                    ErrorText = $"Неизвестная ошибка при выполнении запроса к 1С: {ex.Message}"
                };
            }
        }

        private Employee1CModel? ParseSoapResponse(string soapXml, string cardNumber)
        {
            // Парсим XML
            XmlDocument xmlDocument = new XmlDocument();
            string? jsonString;

            try
            {
                xmlDocument.LoadXml(soapXml);

                XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(xmlDocument.NameTable);
                xmlNamespaceManager.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
                xmlNamespaceManager.AddNamespace("m", "emp_data");

                // Достаём JSON-строку из тега <m:return>
                jsonString = xmlDocument.SelectSingleNode("//m:return", xmlNamespaceManager)?.InnerText;
            }
            catch (Exception ex)
            {
                return new Employee1CModel() { ErrorCode = (int)Employee1CModel.ErrorCodes.SpecificError, ErrorText = ex.Message };
            }

            if (string.IsNullOrWhiteSpace(jsonString))
                return new Employee1CModel() { ErrorCode = (int)Employee1CModel.ErrorCodes.UnknownError, ErrorText = "Неизвестная ошибка." };

            Employee1CModel? employee = new Employee1CModel();

            try
            {
                // Десериализуем JSON в объект
                employee = JsonSerializer.Deserialize<Employee1CModel>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return new Employee1CModel() { ErrorCode = (int)Employee1CModel.ErrorCodes.EmployeeNotFound, ErrorText = "Работник не найден." };
            }

            employee.CardNumber = cardNumber;

            return employee;
        }
    }
}