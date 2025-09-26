using Microsoft.EntityFrameworkCore;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InspectionWorkApp.Models;

namespace InspectionWorkApp.Jobs
{
    public class GenerateAssignmentsJob : IJob
    {
        private readonly YourDbContext _db;
        private readonly TimeSpan _dayShiftDueTime = TimeSpan.FromHours(8); // 08:00 для дневной смены
        private readonly TimeSpan _nightShiftDueTime = TimeSpan.FromHours(20); // 20:00 для ночной смены
        private readonly int _koWorkTypeId = 2; // Id для КО-работ
        private readonly DateTime _defaultExecutionTime = new DateTime(1900, 1, 1); // Безопасное значение для DATETIME

        // Словарь соответствия TOWork.Id и FreqId
        private readonly Dictionary<int, int> _workFreqMap = new Dictionary<int, int>
        {
            { 1, 1 }, { 2, 1 }, { 3, 1 }, { 4, 2 }, { 5, 2 }, { 6, 2 }, { 7, 1 }, { 8, 2 },
            { 9, 1 }, { 10, 1 }, { 11, 1 }, { 12, 1 }, { 13, 1 }, { 14, 1 }, { 15, 2 },
            { 16, 5 }, { 17, 5 }, { 18, 5 }, { 19, 4 }, { 20, 1 }, { 21, 3 }
        };

        public GenerateAssignmentsJob(YourDbContext db)
        {
            _db = db;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                var now = DateTime.Now;
                var today = now.Date;
                var isDayShift = now.Hour >= 8 && now.Hour < 20; // Дневная: 08:00–20:00

                // Получаем все работы и сектора
                var works = await _db.TOWorks.ToListAsync();
                var sectors = await _db.dic_Sector.ToListAsync();

                int addedAssignments = 0;

                foreach (var work in works)
                {
                    // Пропускаем, если TOWork.Id не в словаре
                    if (!_workFreqMap.TryGetValue(work.Id, out var freqId))
                    {
                        Console.WriteLine($"Предупреждение: Частота для WorkId={work.Id} не найдена, пропускаем.");
                        continue;
                    }

                    // Проверяем, существует ли частота
                    var freq = await _db.TOWorkFrequencies.FirstOrDefaultAsync(f => f.Id == freqId);
                    if (freq == null)
                    {
                        Console.WriteLine($"Ошибка: Частота с Id={freqId} для WorkId={work.Id} не найдена, пропускаем.");
                        continue;
                    }

                    // Определяем RoleId
                    var roleId = work.Id >= 1 && work.Id <= 14 ? 1 : 2;

                    // Определяем WorkTypeId (уточните правило, если нужно)
                    var workTypeId = work.Id >= 1 && work.Id <= 14 ? 2 : 1;

                    foreach (var sector in sectors)
                    {
                        var existingAssignment = await _db.TOWorkAssignments
                            .FirstOrDefaultAsync(a => a.WorkId == work.Id && a.SectorId == sector.Id);
                        if (existingAssignment != null)
                        {
                            continue;
                        }

                        var assignment = new WorkAssignment
                        {
                            WorkId = work.Id,
                            FreqId = freqId,
                            RoleId = roleId,
                            WorkTypeId = workTypeId,
                            SectorId = sector.Id,
                            IsCanceled = false,
                            LastExecTime = null // Ночная: 20:00 текущего или предыдущего дня
                        };
                        _db.TOWorkAssignments.Add(assignment);
                        await _db.SaveChangesAsync(); // Сохраняем assignment для получения Id
                        addedAssignments++;

                        
                            var nextDue = isDayShift
                                ? today.AddHours(20) // Ночная: 20:00 текущего дня
                                : today.AddDays(1).AddHours(8); // Дневная: 08:00 следующего дня

                            // Проверяем, чтобы nextDue не был в прошлом
                            if (nextDue <= now)
                            {
                                nextDue = nextDue.AddDays(1); // Сдвигаем на сутки
                            }

                            //var execution = new Execution
                            //{
                            //    AssignmentId = assignment.Id, // Теперь Id валидный
                            //    OperatorId = null,
                            //    ExecutionTime = _defaultExecutionTime,
                            //    Status = 2,
                            //    DueDateTime = nextDue
                            //};
                            //_db.TOExecutions.Add(execution);
                        
                    }
                }

                if (addedAssignments > 0)
                {
                    await _db.SaveChangesAsync();
                    Console.WriteLine($"Создано {addedAssignments} новых назначений в {DateTime.Now}");
                }
                else
                {
                    Console.WriteLine("Новые назначения не созданы: все комбинации Work/Sector уже существуют.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в GenerateAssignmentsJob: {ex.Message}");
                throw;
            }
        }
    }
}