using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner;
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Meal> Meals => Set<Meal>();
    public DbSet<WeekPlanEntry> WeekPlanEntries => Set<WeekPlanEntry>();

    public DbSet<Settings> Settings => Set<Settings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Slot>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Meal>().HasIndex(x => x.MealId).IsUnique();

        b.Entity<WeekPlanEntry>()
            .HasIndex(x => new { x.Date, x.SlotKey })
            .IsUnique();

        b.Entity<WeekPlanEntry>()
            .Property(x => x.PortionMultiplier)
            .HasPrecision(6, 2);

        b.AddSettings();
    }
}
