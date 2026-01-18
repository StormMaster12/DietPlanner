using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Slots;

public record Slot(
    SlotKey Key,
    string DisplayName,
    int SortOrder);

public static class ConfigureSlot
{
    public static ModelBuilder ConfigureSlotEntity(this ModelBuilder builder)
    {
        builder.Entity<Slot>(b =>
        {
            b.HasKey(x => x.Key);
        });
        return builder;
    }
}