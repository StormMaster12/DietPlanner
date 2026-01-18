using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.Meal;

public sealed class MealEntry
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public SlotKey SlotKey { get; set; }
    public Slot Slot { get; set; } = null!;

    public int Kcal { get; set; }
    public int ProteinG { get; set; }
    public int FibreG { get; set; }
    public int Plants { get; set; }

    public string? ZoeNotes { get; set; }
    public required string MfName { get; set; }
    public string? Notes { get; set; }
}
