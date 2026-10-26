namespace DietPlanner.Endpoints.Meal;

/// <summary>
/// The rows a CSV or PDF import managed to parse, plus a message for each row it couldn't. Both are
/// passed on to <see cref="MealImportUpsert"/>, which saves the rows and reports the errors.
/// </summary>
internal sealed record ParsedMealRows(List<MealImportRow> Rows, List<string> Errors);
