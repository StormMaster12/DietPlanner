namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// A single meal's details plus how far the day it's planned on is below its fibre/plant
/// targets, sent to an LLM so it can suggest additions that close the gap.
/// </summary>
public sealed record MealAdditionSuggestionRequest(
    string MealName,
    string? ZoeNotes,
    string? Notes,
    IReadOnlyList<DayPlanIngredientDto> Ingredients,
    int FibreShortfallG,
    int PlantsShortfall);
