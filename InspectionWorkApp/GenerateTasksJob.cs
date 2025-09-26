using Microsoft.EntityFrameworkCore;
using Quartz;
using System;
using System.Linq;
using System.Threading.Tasks;
using InspectionWorkApp.Models;

namespace InspectionWorkApp.Jobs
{
    public class GenerateTasksJob : IJob
    {
        private readonly YourDbContext _db;
        private readonly int _koWorkTypeId = 2; // Id для КО работ
        private readonly DateTime _defaultExecutionTime = new DateTime(1900, 1, 1); // Безопасное значение для DATETIME

        public GenerateTasksJob(YourDbContext db)
        {
            _db = db;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                var today = DateTime.Today;
                var hoursPerDay = 12.0;

                // Пометить просроченные задачи
                var overdueTasks = await _db.TOExecutions
                    .Where(e => e.Status == 2 && e.DueDateTime < DateTime.Now)
                    .ToListAsync();
                foreach (var task in overdueTasks)
                {
                    task.Status = 4; // Просрочена
                }

                // Создание задач для КО работ
                var assignments = await _db.TOWorkAssignments
                    .Include(a => a.Freq)
                    .Include(a => a.WorkType)
                    .Where(a => a.WorkTypeId == _koWorkTypeId && !a.IsCanceled)
                    .ToListAsync();

                foreach (var assignment in assignments)
                {
                    var lastExec = assignment.LastExecTime ?? _defaultExecutionTime;
                    var intervalDays = assignment.Freq.IntervalDay ?? 1;
                    var intervalHours = assignment.Freq.IntervalHour ?? 12;
                    var intervalInDays = Math.Min(intervalDays, intervalHours / hoursPerDay);
                    var nextDue = lastExec.AddDays(intervalInDays);

                    if (nextDue.Date <= today)
                    {
                        var dueDateTime = today.AddHours(8); // Фиксированное время 08:00
                        var existingTask = await _db.TOExecutions
                            .FirstOrDefaultAsync(e => e.AssignmentId == assignment.Id
                                                  && e.DueDateTime.HasValue && e.DueDateTime.Value.Date == today
                                                  && e.DueDateTime.Value.Hour == 8
                                                  && e.Status == 2);
                        if (existingTask == null)
                        {
                            var plannedExecution = new Execution
                            {
                                AssignmentId = assignment.Id,
                                OperatorId = null,
                                ExecutionTime = _defaultExecutionTime,
                                Status = 2,
                                DueDateTime = dueDateTime
                            };
                            _db.TOExecutions.Add(plannedExecution);
                        }
                        if (assignment.LastExecTime == null)
                        {
                            assignment.LastExecTime = today;
                        }
                    }
                }
                await _db.SaveChangesAsync();
                Console.WriteLine($"GenerateTasksJob выполнен успешно в {DateTime.Now}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в GenerateTasksJob: {ex.Message}");
                throw;
            }
        }
    }
}