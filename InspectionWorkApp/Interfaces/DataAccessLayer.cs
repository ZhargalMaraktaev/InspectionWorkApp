using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using InspectionWorkApp.Interfaces;
using InspectionWorkApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InspectionWorkApp.Services
{
    public class DataAccessLayer : IEmployeeRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<DataAccessLayer> _logger;

        public DataAccessLayer(IConfiguration configuration, ILogger<DataAccessLayer> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration), "Connection string not found.");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Employee1CModel> GetEmployeeAsync(string cardNumber)
        {
            try
            {
                Employee1CModel employee = null;
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("SELECT idCard, TabNumber, FIO, Department, EmployName, TORoleId FROM Pilot.dbo.dic_SKUD WHERE idCard = @IdCard", conn);
                cmd.Parameters.Add("@IdCard", SqlDbType.NVarChar).Value = cardNumber;

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string employName = reader["EmployName"].ToString();
                    int? toRoleId = reader["TORoleId"] != DBNull.Value ? Convert.ToInt32(reader["TORoleId"]) : null;

                    employee = new Employee1CModel
                    {
                        CardNumber = reader["idCard"].ToString(),
                        PersonnelNumber = reader["TabNumber"].ToString(),
                        FullName = reader["FIO"].ToString(),
                        Department = reader["Department"].ToString(),
                        Position = employName,
                        TORoleId = toRoleId,
                        ErrorCode = (int)Employee1CModel.ErrorCodes.ReadingSuccessful
                    };
                }

                // Закрываем reader перед выполнением нового запроса
                reader.Close();

                if (employee != null && !employee.TORoleId.HasValue)
                {
                    employee.TORoleId = DetermineRoleFromEmployName(employee.Position);
                    if (employee.TORoleId.HasValue)
                    {
                        await UpdateTORoleIdAsync(conn, cardNumber, employee.TORoleId.Value);
                    }
                }

                if (employee != null)
                {
                    return employee;
                }

                _logger.LogInformation("Employee not found for cardNumber: {CardNumber}", cardNumber);
                return new Employee1CModel
                {
                    CardNumber = cardNumber,
                    ErrorCode = (int)Employee1CModel.ErrorCodes.EmployeeNotFound,
                    ErrorText = "Employee not found in dic_SKUD."
                };
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error in GetEmployeeAsync for cardNumber: {CardNumber}", cardNumber);
                return new Employee1CModel
                {
                    CardNumber = cardNumber,
                    ErrorCode = (int)Employee1CModel.ErrorCodes.SpecificError,
                    ErrorText = $"Database error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetEmployeeAsync for cardNumber: {CardNumber}", cardNumber);
                return new Employee1CModel
                {
                    CardNumber = cardNumber,
                    ErrorCode = (int)Employee1CModel.ErrorCodes.UnknownError,
                    ErrorText = $"Unexpected error: {ex.Message}"
                };
            }
        }

        public async Task SaveEmployeeAsync(Employee1CModel employee)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                if (await CheckCardIdAsync(conn, employee.PersonnelNumber))
                {
                    _logger.LogInformation("Employee with personnelNumber {PersonnelNumber} already exists.", employee.PersonnelNumber);
                    return;
                }

                // Определяем TORoleId на основе EmployName
                int? toRoleId = DetermineRoleFromEmployName(employee.Position);

                using var cmd = new SqlCommand(@"
                    INSERT INTO Pilot.dbo.dic_SKUD (idCard, TabNumber, FIO, Department, EmployName, TORoleId)
                    VALUES (@IdCard, @TabNumber, @FIO, @Department, @Position, @TORoleId)", conn);
                cmd.Parameters.Add("@IdCard", SqlDbType.NVarChar).Value = employee.CardNumber ?? (object)DBNull.Value;
                cmd.Parameters.Add("@TabNumber", SqlDbType.NVarChar).Value = employee.PersonnelNumber ?? (object)DBNull.Value;
                cmd.Parameters.Add("@FIO", SqlDbType.NVarChar).Value = employee.FullName ?? (object)DBNull.Value;
                cmd.Parameters.Add("@Department", SqlDbType.NVarChar).Value = employee.Department ?? (object)DBNull.Value;
                cmd.Parameters.Add("@Position", SqlDbType.NVarChar).Value = employee.Position ?? (object)DBNull.Value;
                cmd.Parameters.Add("@TORoleId", SqlDbType.Int).Value = toRoleId ?? (object)DBNull.Value;

                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Employee saved successfully: {PersonnelNumber}, TORoleId={TORoleId}", employee.PersonnelNumber, toRoleId ?? null);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error in SaveEmployeeAsync for personnelNumber: {PersonnelNumber}", employee.PersonnelNumber);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SaveEmployeeAsync for personnelNumber: {PersonnelNumber}", employee.PersonnelNumber);
                throw;
            }
        }

        public async Task<int?> GetOperatorIdAsync(string personnelNumber)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("SELECT id FROM Pilot.dbo.dic_SKUD WHERE TabNumber = @TabNumber", conn);
                cmd.Parameters.Add("@TabNumber", SqlDbType.NVarChar).Value = personnelNumber ?? (object)DBNull.Value;

                var result = await cmd.ExecuteScalarAsync();
                if (result != null)
                {
                    _logger.LogInformation("OperatorId found for personnelNumber: {PersonnelNumber}", personnelNumber);
                    return Convert.ToInt32(result);
                }

                _logger.LogInformation("OperatorId not found for personnelNumber: {PersonnelNumber}", personnelNumber);
                return null;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error in GetOperatorIdAsync for personnelNumber: {PersonnelNumber}", personnelNumber);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetOperatorIdAsync for personnelNumber: {PersonnelNumber}", personnelNumber);
                throw;
            }
        }

        private async Task<bool> CheckCardIdAsync(SqlConnection conn, string personnelNumber)
        {
            try
            {
                using var cmd = new SqlCommand("SELECT COUNT(*) FROM Pilot.dbo.dic_SKUD WHERE TabNumber = @TabNumber", conn);
                cmd.Parameters.Add("@TabNumber", SqlDbType.NVarChar).Value = personnelNumber ?? (object)DBNull.Value;
                var count = (int)await cmd.ExecuteScalarAsync();
                return count > 0;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error in CheckCardIdAsync for personnelNumber: {PersonnelNumber}", personnelNumber);
                throw;
            }
        }

        private int? DetermineRoleFromEmployName(string employName)
        {
            if (string.IsNullOrEmpty(employName))
            {
                _logger.LogWarning("EmployName is null or empty, cannot determine TORoleId");
                return null;
            }

            employName = employName.ToLower();
            if (employName.Contains("слесарь"))
            {
                _logger.LogInformation("Assigned TORoleId=2 for EmployName: {EmployName}", employName);
                return 2;
            }
            else if (employName.Contains("оператор"))
            {
                _logger.LogInformation("Assigned TORoleId=1 for EmployName: {EmployName}", employName);
                return 1;
            }
            else if (employName.Contains("инженер") || employName.Contains("мастер") ||
                     employName.Contains("начальник") || employName.Contains("заместитель"))
            {
                _logger.LogInformation("Assigned TORoleId=4 for EmployName: {EmployName}", employName);
                return 4;
            }

            _logger.LogInformation("No matching role found for EmployName: {EmployName}", employName);
            return null;
        }

        private async Task UpdateTORoleIdAsync(SqlConnection conn, string cardNumber, int toRoleId)
        {
            try
            {
                using var cmd = new SqlCommand("UPDATE Pilot.dbo.dic_SKUD SET TORoleId = @TORoleId WHERE idCard = @IdCard", conn);
                cmd.Parameters.Add("@TORoleId", SqlDbType.Int).Value = toRoleId;
                cmd.Parameters.Add("@IdCard", SqlDbType.NVarChar).Value = cardNumber;
                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Updated TORoleId={TORoleId} for cardNumber: {CardNumber}", toRoleId, cardNumber);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error in UpdateTORoleIdAsync for cardNumber: {CardNumber}", cardNumber);
                throw;
            }
        }
    }
}