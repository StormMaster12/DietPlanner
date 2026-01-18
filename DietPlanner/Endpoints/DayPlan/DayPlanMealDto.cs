using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.DayPlan;

public sealed record DayPlanMealDto(
    DateOnly Date,
    SlotKey SlotKey,
    int SlotOrder,
    MealId MealId,
    string Name,
    string MfName,
    decimal PortionMultiplier,
    int Kcal,
    int ProteinG,
    int FibreG,
    int Plants,
    string? ZoeNotes,
    string? Notes);
