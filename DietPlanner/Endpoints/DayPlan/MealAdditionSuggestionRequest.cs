namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// Which meal to suggest additions for, plus how far the day it's planned on is below its
/// fibre/plant targets so the LLM can size its suggestions to close the gap. The meal's own
/// contents are read from the database, so suggestions are always based on a single portion.
/// </summary>
public sealed record MealAdditionSuggestionRequest(
    Guid MealId,
    int FibreShortfallG,
    int PlantsShortfall);
