using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// The most recent AI-grouped weekly shopping list, plus a fingerprint of the ingredient lines
/// that produced it. Lets re-displaying an unchanged week's grouping skip the Anthropic API call.
/// </summary>
public sealed class ShoppingListGroupingCacheEntry
{
    public DateOnly WeekStartDate { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string GroupedJson { get; set; }
    public required string ErrorsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public static class ConfigureShoppingListGroupingCacheEntry
{
    public static ModelBuilder ConfigureShoppingListGroupingCacheEntryEntity(this ModelBuilder builder)
    {
        builder.Entity<ShoppingListGroupingCacheEntry>().HasKey(x => x.WeekStartDate);
        return builder;
    }
}
