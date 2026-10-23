using DietPlanner.Endpoints.Meal;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// The AI-suggested additions currently saved for a meal, plus a fingerprint of the meal contents
/// they were generated from. Keyed by meal rather than by day so the same meal reuses one set of
/// suggestions wherever it's planned; once the meal itself is edited the fingerprint no longer
/// matches and the set is treated as absent until it's regenerated.
/// </summary>
public sealed class MealAdditionSuggestionSet
{
    public Guid MealId { get; set; }
    public MealEntry Meal { get; set; } = null!;

    public required string MealFingerprint { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<MealSuggestedAddition> Additions { get; set; } = new();
}

public static class ConfigureMealAdditionSuggestionSet
{
    public static ModelBuilder ConfigureMealAdditionSuggestionSetEntity(this ModelBuilder builder)
    {
        builder.Entity<MealAdditionSuggestionSet>(b =>
        {
            b.HasKey(x => x.MealId);
            b.HasOne(x => x.Meal)
                .WithOne()
                .HasForeignKey<MealAdditionSuggestionSet>(x => x.MealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return builder;
    }
}
