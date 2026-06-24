namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// Outcome of importing an uploaded meal CSV file: how many rows became brand new meals, how many
/// updated an existing meal that already had the same name and slot, and any rows that could not be
/// parsed or saved at all (reported one-based to match what the user sees in a spreadsheet).
/// </summary>
public sealed record MealImportResult(
    int InsertedCount,
    int UpdatedCount,
    IReadOnlyList<string> RowErrors);
