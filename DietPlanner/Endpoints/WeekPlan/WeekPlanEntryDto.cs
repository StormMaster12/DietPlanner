using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.WeekPlan;

public sealed record WeekPlanEntryDto(
    DateOnly Date,
    SlotKey SlotKey,
    Guid MealId,
    decimal PortionMultiplier,
    string? Notes);
