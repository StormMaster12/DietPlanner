using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// The most recent AI-generated addition suggestions for a meal, plus a fingerprint of the inputs
/// that produced them. Lets a repeat request for the same meal skip the Anthropic API call
/// entirely when nothing relevant (ingredients, notes, fibre/plant shortfall) has changed.
/// </summary>
public sealed class MealAdditionSuggestionCacheEntry
{
    public Guid MealId { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string SuggestionsJson { get; set; }
    public required string ErrorsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public static class ConfigureMealAdditionSuggestionCacheEntry
{
    public static ModelBuilder ConfigureMealAdditionSuggestionCacheEntryEntity(this ModelBuilder builder)
    {
        builder.Entity<MealAdditionSuggestionCacheEntry>().HasKey(x => x.MealId);
        return builder;
    }
}
