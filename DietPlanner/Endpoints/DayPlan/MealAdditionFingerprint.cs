using System.Text.Json;
using DietPlanner.Endpoints.Meal;

namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// Identifies a meal's suggestion-relevant contents (name, notes, ingredients) so saved additions
/// can be reused for an unchanged meal and ignored once the meal has been edited. Deliberately
/// excludes the day's fibre/plant shortfall and portion size: the same meal should reuse the same
/// suggestions on any day rather than spending tokens regenerating them.
/// </summary>
public static class MealAdditionFingerprint
{
    public static string Compute(MealEntry meal)
    {
        var ingredients = meal.Ingredients
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .ThenBy(i => i.Unit, StringComparer.Ordinal)
            .ThenBy(i => i.Quantity)
            .Select(i => new { i.Name, i.Quantity, i.Unit });

        return JsonSerializer.Serialize(new { meal.Name, meal.ZoeNotes, meal.Notes, Ingredients = ingredients });
    }
}
