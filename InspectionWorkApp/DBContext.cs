using InspectionWorkApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InspectionWorkApp
{
    public class YourDbContext : DbContext
    {
        public DbSet<Sector> dic_Sector { get; set; }
        public DbSet<Role> TORoles { get; set; }
        public DbSet<Skud> dic_SKUD { get; set; }
        public DbSet<Work> TOWorks { get; set; }
        public DbSet<TOWorkTypes> TOWorkTypes { get; set; }
        public DbSet<WorkFrequency> TOWorkFrequencies { get; set; }
        public DbSet<TOStatuses> TOStatuses { get; set; }
        public DbSet<WorkAssignment> TOWorkAssignments { get; set; }
        public DbSet<Execution> TOExecutions { get; set; }
        public DbSet<PCNameSector> dic_PCNameSector { get; set; } // Добавлено

        public YourDbContext(DbContextOptions<YourDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Sector>().ToTable("dic_Sector", "dbo");
            modelBuilder.Entity<Role>().ToTable("TORoles", "dbo");
            modelBuilder.Entity<Skud>().ToTable("dic_SKUD", "dbo");
            modelBuilder.Entity<Work>().ToTable("TOWorks", "dbo");
            modelBuilder.Entity<TOWorkTypes>().ToTable("TOWorkTypes", "dbo");
            modelBuilder.Entity<WorkFrequency>().ToTable("TOWorkFrequencies", "dbo");
            modelBuilder.Entity<TOStatuses>().ToTable("TOStatuses", "dbo");
            modelBuilder.Entity<WorkAssignment>().ToTable("TOWorkAssignments", "dbo");
            modelBuilder.Entity<Execution>().ToTable("TOExecutions", "dbo");
            modelBuilder.Entity<PCNameSector>().ToTable("dic_PCNameSector", "dbo"); // Добавлено

            modelBuilder.Entity<WorkAssignment>()
                .HasOne(wa => wa.Work)
                .WithMany()
                .HasForeignKey(wa => wa.WorkId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WorkAssignment>()
                .HasOne(wa => wa.Freq)
                .WithMany()
                .HasForeignKey(wa => wa.FreqId);

            modelBuilder.Entity<WorkAssignment>()
                .HasOne(wa => wa.Role)
                .WithMany()
                .HasForeignKey(wa => wa.RoleId);

            modelBuilder.Entity<WorkAssignment>()
                .HasOne(wa => wa.WorkType)
                .WithMany()
                .HasForeignKey(wa => wa.WorkTypeId);

            modelBuilder.Entity<WorkAssignment>()
                .HasOne(wa => wa.Sector)
                .WithMany()
                .HasForeignKey(wa => wa.SectorId);

            modelBuilder.Entity<Execution>()
                .HasOne(e => e.Assignment)
                .WithMany()
                .HasForeignKey(e => e.AssignmentId);

            modelBuilder.Entity<Execution>()
                .HasOne(e => e.status)
                .WithMany()
                .HasForeignKey(e => e.Status);

            modelBuilder.Entity<Execution>()
                .HasOne(e => e.Operator)
                .WithMany()
                .HasForeignKey(e => e.OperatorId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Skud>()
                .HasOne(s => s.Role)
                .WithMany()
                .HasForeignKey(s => s.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Skud>()
                .HasOne(s => s.Role)
                .WithMany()
                .HasForeignKey(s => s.TORoleId)
                .OnDelete(DeleteBehavior.SetNull);

            // Добавлено: конфигурация для PCNameSector
            modelBuilder.Entity<PCNameSector>()
                .HasOne(p => p.SectorNavigation)
                .WithMany()
                .HasForeignKey(p => p.Sector)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WorkAssignment>()
                .HasIndex(wa => new { wa.RoleId, wa.SectorId, wa.IsCanceled });

            modelBuilder.Entity<WorkAssignment>()
                .HasIndex(wa => new { wa.WorkId, wa.SectorId })
                .IsUnique();

            modelBuilder.Entity<Execution>()
                .HasIndex(e => new { e.AssignmentId, e.DueDateTime });

            modelBuilder.Entity<Execution>()
                .Property(e => e.ExecutionTime)
                .HasColumnType("datetime")
                .HasDefaultValue(new DateTime(1900, 1, 1));

            modelBuilder.Entity<Execution>()
                .Property(e => e.DueDateTime)
                .HasColumnType("datetime");
        }
    }
}