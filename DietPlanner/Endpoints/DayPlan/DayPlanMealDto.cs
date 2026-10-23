using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.DayPlan;

/// <param name="SuggestedAdditions">
/// The saved AI-suggested additions for this meal, scaled to this entry's portion. Null when none
/// have been generated for the meal's current contents; empty when the AI suggested nothing.
/// </param>
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
    IReadOnlyList<DayPlanIngredientDto> Ingredients,
    IReadOnlyList<SuggestedMealAddition>? SuggestedAdditions);
