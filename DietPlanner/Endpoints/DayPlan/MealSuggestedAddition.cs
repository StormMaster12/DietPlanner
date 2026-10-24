using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// One saved AI-suggested ingredient (e.g. "1 tbsp Chia seeds") for a meal, stored for a single
/// portion of that meal so it can be scaled the same way as the meal's own ingredients.
/// </summary>
public sealed class MealSuggestedAddition
{
    public Guid Id { get; set; }

    public Guid MealId { get; set; }
    public MealAdditionSuggestionSet SuggestionSet { get; set; } = null!;

    /// <summary>Preserves the order the AI suggested them in.</summary>
    public int SortOrder { get; set; }

    public required string Name { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public required string Reason { get; set; }
}

public static class ConfigureMealSuggestedAddition
{
    public static ModelBuilder ConfigureMealSuggestedAdditionEntity(this ModelBuilder builder)
    {
        builder.Entity<MealSuggestedAddition>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.SuggestionSet)
                .WithMany(s => s.Additions)
                .HasForeignKey(x => x.MealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return builder;
    }
}
