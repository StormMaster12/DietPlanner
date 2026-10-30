using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.Meal;

/// <param name="SuggestedAdditions">
/// The saved AI-suggested additions for a single portion of this meal. Null when none have been
/// generated for the meal's current contents; empty when the AI suggested nothing.
/// </param>
public sealed record MealDto(
Guid MealId,
string Name,
SlotKey SlotKey,
int Kcal,
int ProteinG,
int CarbsG,
int FibreG,
int Plants,
string? ZoeNotes,
string MfName,
string? Notes,
IReadOnlyList<MealIngredientDto> Ingredients,
IReadOnlyList<SuggestedMealAddition>? SuggestedAdditions);
