namespace DietPlanner.Endpoints.DayPlan;

public static class WeeklyShoppingListBuilder
{
    /// <summary>
    /// Combines every ingredient - and every saved suggested addition - across every meal in the
    /// week, summing the quantities of any lines that share the same name and unit (e.g. two
    /// different meals both calling for "g" of "Chicken breast" become a single combined line).
    /// </summary>
    public static List<DayPlanIngredientDto> Build(IEnumerable<DayPlanResponseDto> dayPlans)
    {
        return dayPlans
            .SelectMany(d => d.Meals)
            .SelectMany(m => m.Ingredients.Concat(
                (m.SuggestedAdditions ?? []).Select(a => new DayPlanIngredientDto(a.Ingredient, a.Quantity, a.Unit))))
            .GroupBy(i => (i.Name, i.Unit))
            .Select(g => new DayPlanIngredientDto(g.Key.Name, g.Sum(i => i.Quantity), g.Key.Unit))
            .OrderBy(i => i.Name)
            .ToList();
    }
}
