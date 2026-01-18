using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.WeekPlan;

public sealed record WeekPlanEntry(
    Guid Id,
    DateOnly Date,
    SlotKey SlotKey,
    Guid MealId,
    decimal PortionMultiplier,
    string? Notes)
{
    public MealEntry Meal { get; set; } = null!;
    public Slot Slot { get; set; } = null!;
}

public static class ConfigureWeekPlanEntry
{
    public static ModelBuilder AddWeekPlanEntry(this ModelBuilder b)
    {
        b.Entity<WeekPlanEntry>()
        .HasIndex(x => new { x.Date, x.SlotKey })
        .IsUnique();

        b.Entity<WeekPlanEntry>()
            .Property(x => x.PortionMultiplier)
            .HasPrecision(6, 2);

        b.Entity<WeekPlanEntry>().HasOne(x => x.Meal)
            .WithMany()
            .HasForeignKey(x => x.MealId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<WeekPlanEntry>().HasOne(x => x.Slot)
            .WithMany()
            .HasForeignKey(x => x.SlotKey)
            .OnDelete(DeleteBehavior.Restrict);

        return b;
    }
}
