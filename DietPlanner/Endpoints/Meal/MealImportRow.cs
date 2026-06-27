using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// One successfully parsed row from an uploaded meal CSV file, ready to be upserted into the
/// database. Rows that failed to parse never become a <see cref="MealImportRow"/> - they are
/// reported separately as row-numbered error strings instead.
/// </summary>
/// <summary>
/// Ingredients is null when the import source didn't supply ingredient data at all (e.g. a CSV
/// without an Ingredients column), meaning any existing meal's ingredients should be left
/// untouched. An empty (non-null) list means the source explicitly described no ingredients, and
/// existing ingredients should be cleared to match.
/// </summary>
public sealed record MealImportRow(
    string Name,
    SlotKey SlotKey,
    int Kcal,
    int ProteinG,
    int CarbsG,
    int FibreG,
    int Plants,
    string MfName,
    string? ZoeNotes,
    string? Notes,
    IReadOnlyList<MealImportIngredientRow>? Ingredients = null);
