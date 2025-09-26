using InspectionWorkApp.Controllers;
using InspectionWorkApp.Interfaces;
using InspectionWorkApp.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace InspectionWorkApp.Services
{
    public class OperatorService
    {
        private readonly IEmployeeRepository _employeeRepository;
        private readonly Controller1C _controller1C;
        private readonly COMController _comController;
        private readonly ILogger<OperatorService> _logger;
        private Employee1CModel _currentOperator;

        public event EventHandler OnOperatorChanged;

        public Employee1CModel CurrentOperator
        {
            get => _currentOperator;
            private set
            {
                _currentOperator = value;
                _logger.LogInformation("CurrentOperator changed: {PersonnelNumber}", _currentOperator?.PersonnelNumber ?? "null");
                OnOperatorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public OperatorService(
            IEmployeeRepository employeeRepository,
            Controller1C controller1C,
            ILogger<OperatorService> logger,
            COMController comController)
        {
            _employeeRepository = employeeRepository ?? throw new ArgumentNullException(nameof(employeeRepository));
            _controller1C = controller1C ?? throw new ArgumentNullException(nameof(controller1C));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _comController = comController ?? throw new ArgumentNullException(nameof(comController));

            _comController.StateChanged += ComController_StateChanged;
            _logger.LogInformation("OperatorService initialized");
        }

        private async void ComController_StateChanged(object sender, COMEventArgs.ReadingDataEventArgs e)
        {
            try
            {
                _logger.LogInformation("COM event received: State={State}, CardId={CardId}", e.State, e.CardId);
                if (e.State == COMControllerParamsModel.COMStates.Detected && !string.IsNullOrEmpty(e.CardId))
                {
                    _logger.LogInformation("Processing card ID: {CardId}", e.CardId);
                    await AuthenticateOperatorAsync(e.CardId, true, DateTime.Now);
                }
                else if (e.State == COMControllerParamsModel.COMStates.Removed)
                {
                    _logger.LogInformation("Card removed, deauthenticating operator.");
                    await AuthenticateOperatorAsync(null, false, DateTime.Now);
                }
                else if (e.State == COMControllerParamsModel.COMStates.None)
                {
                    _logger.LogWarning("COM port error: {ErrorText}", e.ErrorText);
                    await AuthenticateOperatorAsync(null, false, DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing COM event: {State}", e.State);
            }
        }

        public async Task AuthenticateOperatorAsync(string cardNumber, bool isAuth, DateTime? authTime)
        {
            try
            {
                _logger.LogInformation("AuthenticateOperatorAsync called: CardNumber={CardNumber}, IsAuth={IsAuth}, AuthTime={AuthTime}", cardNumber, isAuth, authTime);
                Employee1CModel current = null;
                if (isAuth)
                {
                    await InitializeOperatorAsync(cardNumber);
                    current = CurrentOperator;
                    if (current == null)
                    {
                        _logger.LogWarning("Failed to initialize operator for cardNumber: {CardNumber}", cardNumber);
                        return;
                    }
                }
                else
                {
                    current = CurrentOperator;
                    if (current == null)
                    {
                        _logger.LogWarning("No operator to deauthenticate for cardNumber: {CardNumber}", cardNumber);
                        return;
                    }
                }

                int? operatorId = await _employeeRepository.GetOperatorIdAsync(current.PersonnelNumber);
                if (!operatorId.HasValue)
                {
                    _logger.LogWarning("OperatorId not found for personnelNumber: {PersonnelNumber}", current.PersonnelNumber);
                    CurrentOperator = null;
                    return;
                }

                // Удалён вызов UpdateOperatorIdExchangeAsync, так как таблица Exchange не существует

                DateTime? authFrom = isAuth ? authTime : null;
                DateTime? authTo = isAuth ? null : authTime;

                if (!isAuth)
                {
                    CurrentOperator = null;
                    _logger.LogInformation("Operator deauthenticated: {PersonnelNumber}", current.PersonnelNumber);
                }
                else
                {
                    _logger.LogInformation("Operator authenticated: {PersonnelNumber}", current.PersonnelNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during operator authentication for CardId: {CardId}", cardNumber);
                CurrentOperator = null;
            }
        }

        public async Task<int?> GetOperatorIdAsync(string personnelNumber)
        {
            try
            {
                return await _employeeRepository.GetOperatorIdAsync(personnelNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving OperatorId for personnelNumber: {PersonnelNumber}", personnelNumber);
                return null;
            }
        }

        public async Task InitializeOperatorAsync(string cardNumber)
        {
            try
            {
                _logger.LogInformation("InitializeOperatorAsync called for cardNumber: {CardNumber}", cardNumber);
                CurrentOperator = await _employeeRepository.GetEmployeeAsync(cardNumber) ?? await FetchAndSaveFrom1C(cardNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in InitializeOperatorAsync for cardNumber: {CardNumber}", cardNumber);
                CurrentOperator = null;
            }
        }

        private async Task<Employee1CModel> FetchAndSaveFrom1C(string cardNumber)
        {
            try
            {
                _logger.LogInformation("Fetching employee from 1C for cardNumber: {CardNumber}", cardNumber);
                var employee = await _controller1C.GetResp1CSKUD(cardNumber);
                if (employee.ErrorCode == 0 && !string.IsNullOrEmpty(employee.PersonnelNumber))
                {
                    await _employeeRepository.SaveEmployeeAsync(employee);
                    _logger.LogInformation("Employee fetched and saved from 1C: {PersonnelNumber}", employee.PersonnelNumber);
                    return employee;
                }
                _logger.LogWarning("Failed to fetch employee from 1C for cardNumber: {CardNumber}", cardNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FetchAndSaveFrom1C for cardNumber: {CardNumber}", cardNumber);
                return null;
            }
        }
    }
}