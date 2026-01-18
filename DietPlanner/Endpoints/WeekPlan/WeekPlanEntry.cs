using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.WeekPlan
{
    public sealed record WeekPlanEntry(
        int Id,
        DateOnly Date,
        SlotKey SlotKey,
        MealId MealId,
        MealEntry Meal,
        decimal PortionMultiplier,
        string? Notes);

    public static class ConfigureWeekPlanEntry
    {
        public static ModelBuilder AddWeekPlanEntry(this ModelBuilder b)
        {
            _ = b.Entity<WeekPlanEntry>()
            .HasIndex(x => new { x.Date, x.SlotKey })
            .IsUnique();

            _ = b.Entity<WeekPlanEntry>()
                .Property(x => x.PortionMultiplier)
                .HasPrecision(6, 2);

            _ = b.Entity<WeekPlanEntry>().HasOne(x => x.Meal)
                .WithMany()
                .HasForeignKey(x => x.MealId)
                .OnDelete(DeleteBehavior.Restrict);

            return b;
        }
    }
}

