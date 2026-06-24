using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Meal;

public sealed class MealEntry
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public SlotKey SlotKey { get; set; }
    public Slot Slot { get; set; } = null!;

    public int Kcal { get; set; }
    public int ProteinG { get; set; }
    public int CarbsG { get; set; }
    public int FibreG { get; set; }
    public int Plants { get; set; }

    public string? ZoeNotes { get; set; }
    public required string MfName { get; set; }
    public string? Notes { get; set; }
}

public static class ConfigureMealEntry
{
    public static ModelBuilder ConfigureMealEntryEntity(this ModelBuilder builder)
    {
        builder.Entity<MealEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Slot)
                .WithMany()
                .HasForeignKey(x => x.SlotKey)
                .OnDelete(DeleteBehavior.Restrict);

        });

        return builder;
    }
}