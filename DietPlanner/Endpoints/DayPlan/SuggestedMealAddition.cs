namespace DietPlanner.Endpoints.DayPlan;

/// <summary>An LLM-suggested ingredient addition for a meal, with the reasoning behind it.</summary>
public sealed record SuggestedMealAddition(string Ingredient, decimal Quantity, string? Unit, string Reason)
{
    /// <summary>Scales the quantity by a planned portion multiplier, rounded the same way as the meal's own ingredients.</summary>
    public SuggestedMealAddition ScaledBy(decimal portionMultiplier)
        => this with { Quantity = Math.Round(Quantity * portionMultiplier, 2) };
}
