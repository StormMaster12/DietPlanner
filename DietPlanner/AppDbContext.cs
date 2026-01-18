using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Endpoints.Meal.MealEntry> Meals => Set<Endpoints.Meal.MealEntry>();
    public DbSet<WeekPlanEntry> WeekPlanEntries => Set<WeekPlanEntry>();
    public DbSet<Settings> Settings => Set<Settings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        _ = b.Entity<Slot>().HasIndex(x => x.Key).IsUnique();
        _ = b.Entity<MealEntry>().HasIndex(x => x.MealId).IsUnique();


        _ = b.AddWeekPlanEntry();
        _ = b.AddSettings();
    }
}
