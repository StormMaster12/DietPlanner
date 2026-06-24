using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// One successfully parsed row from an uploaded meal CSV file, ready to be upserted into the
/// database. Rows that failed to parse never become a <see cref="MealImportRow"/> - they are
/// reported separately as row-numbered error strings instead.
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
    string? Notes);
