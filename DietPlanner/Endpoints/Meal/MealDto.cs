using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.Meal;

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
IReadOnlyList<MealIngredientDto> Ingredients);
