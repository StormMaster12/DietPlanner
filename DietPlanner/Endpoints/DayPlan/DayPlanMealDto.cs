using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.DayPlan;

public sealed record DayPlanMealDto(
    DateOnly Date,
    SlotKey SlotKey,
    int SlotOrder,
    Guid MealId,
    string Name,
    string MfName,
    decimal PortionMultiplier,
    int Kcal,
    int ProteinG,
    int CarbsG,
    int FibreG,
    int Plants,
    string? ZoeNotes,
    string? Notes,
    IReadOnlyList<DayPlanIngredientDto> Ingredients);
