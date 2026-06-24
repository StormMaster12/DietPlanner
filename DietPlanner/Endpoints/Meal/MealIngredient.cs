using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Meal;

/// <summary>One ingredient line (e.g. "200 g chicken breast") belonging to a single recipe/meal.</summary>
public sealed class MealIngredient
{
    public Guid Id { get; set; }

    public Guid MealId { get; set; }
    public MealEntry Meal { get; set; } = null!;

    public required string Name { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Unit of measure, e.g. "g", "tbsp", "cup". Null/empty for countable items such as "2 eggs".</summary>
    public string? Unit { get; set; }
}

public static class ConfigureMealIngredient
{
    public static ModelBuilder ConfigureMealIngredientEntity(this ModelBuilder builder)
    {
        builder.Entity<MealIngredient>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Meal)
                .WithMany(m => m.Ingredients)
                .HasForeignKey(x => x.MealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return builder;
    }
}
