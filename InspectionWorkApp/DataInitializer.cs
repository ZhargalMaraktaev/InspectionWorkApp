using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using InspectionWorkApp.Models;

namespace InspectionWorkApp
{
    public class DataInitializer
    {
        private readonly YourDbContext _db;

        public DataInitializer(YourDbContext db)
        {
            _db = db;
        }

        public async Task InitializeAsync()
        {
            // Добавление начальных ролей
            if (!await _db.TORoles.AnyAsync())
            {
                _db.TORoles.AddRange(
                    new Role { Id = 1, RoleName = "Оператор" },
                    new Role { Id = 2, RoleName = "Администратор" }
                );
                await _db.SaveChangesAsync();
            }

            // Добавление начальных секторов
            if (!await _db.dic_Sector.AnyAsync())
            {
                _db.dic_Sector.AddRange(
                    new Sector { Id = 1, SectorName = "Сектор 1" },
                    new Sector { Id = 2, SectorName = "Сектор 2" }
                );
                await _db.SaveChangesAsync();
            }

            // Добавление начальных частот
            if (!await _db.TOWorkFrequencies.AnyAsync())
            {
                _db.TOWorkFrequencies.AddRange(
                    new WorkFrequency { Id = 1, Frequency = "Ежедневно" },
                    new WorkFrequency { Id = 2, Frequency = "Еженедельно" },
                    new WorkFrequency { Id = 3, Frequency = "Ежемесячно" },
                    new WorkFrequency { Id = 4, Frequency = "Раз в квартал" },
                    new WorkFrequency { Id = 5, Frequency = "Раз в год" }
                );
                await _db.SaveChangesAsync();
            }

            // Добавление начальных типов работ
            if (!await _db.TOWorkTypes.AnyAsync())
            {
                _db.TOWorkTypes.AddRange(
                    new TOWorkTypes { Id = 1, WorkType = "Обычная" },
                    new TOWorkTypes { Id = 2, WorkType = "КО" }
                );
                await _db.SaveChangesAsync();
            }
        }
    }
}