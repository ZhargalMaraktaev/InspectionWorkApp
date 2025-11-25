using System.Threading.Tasks;
using InspectionWorkApp.Models;

namespace InspectionWorkApp.Interfaces
{
    public interface IEmployeeRepository
    {
        Task<Employee1CModel> GetEmployeeAsync(string cardNumber);
        Task SaveEmployeeAsync(Employee1CModel employee);
        Task<int?> GetOperatorIdAsync(string personnelNumber);
        Task<Employee1CModel> SyncEmployeeAsync(Employee1CModel employee);
    }
}