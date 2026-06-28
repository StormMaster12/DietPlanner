namespace DietPlanner.Endpoints.DayPlan;

/// <summary>
/// Normalizes ingredient quantities expressed in mass or volume units (g, kg, oz, lb, cup,
/// tbsp, tsp, ml, l - and common spelling variants like "cups" or "lbs") down to grams, so the
/// same ingredient logged with different units across different meals combines into a single
/// shopping-list line instead of several duplicate ones. Countable ingredients (no unit, or
/// units like "clove"/"slice"/"leaf") are left untouched, since a single item is already the
/// most useful unit for those (e.g. "4 chicken breast", "3 egg").
/// </summary>
public static class IngredientUnitConverter
{
    private const decimal OuncesToGrams = 28.3495m;
    private const decimal PoundsToGrams = 453.592m;
    private const decimal MillilitresPerCup = 236.588m;
    private const decimal TablespoonsPerCup = 16m;
    private const decimal TeaspoonsPerCup = 48m;

    /// <summary>
    /// Grams per US cup for ingredients whose density differs meaningfully from water.
    /// Matched against the ingredient name by longest-keyword-wins substring search, so more
    /// specific entries (e.g. "all-purpose flour") take priority over general ones ("flour").
    /// Anything not listed here falls back to water's density, which is a reasonable estimate
    /// for most other liquids.
    /// </summary>
    private static readonly (string Keyword, decimal GramsPerCup)[] DensitiesByKeyword =
    {
        ("all-purpose flour", 125m),
        ("coconut flour", 112m),
        ("flour", 125m),
        ("brown sugar", 220m),
        ("sugar", 200m),
        ("cocoa powder", 86m),
        ("cornstarch", 128m),
        ("baking soda", 220m),
        ("salt and pepper", 237m),
        ("salt", 273m),
        ("pepper", 100m),
        ("paprika", 108m),
        ("cinnamon", 116m),
        ("cumin", 90m),
        ("chili powder", 100m),
        ("chili flake", 80m),
        ("garlic powder", 136m),
        ("onion powder", 100m),
        ("ground nutmeg", 115m),
        ("turmeric", 133m),
        ("cayenne pepper", 90m),
        ("cajun seasoning", 96m),
        ("italian seasoning", 30m),
        ("dried oregano", 30m),
        ("dry rosemary", 30m),
        ("fresh grated ginger", 96m),
        ("greek yogurt", 245m),
        ("yogurt", 245m),
        ("buttermilk", 245m),
        ("cashew milk", 240m),
        ("milk", 245m),
        ("ricotta cheese", 246m),
        ("parmesan cheese", 100m),
        ("shredded part-skim mozzarella cheese", 113m),
        ("part skim shredded mozzarella cheese", 113m),
        ("shredded cheddar cheese", 113m),
        ("mozzarella", 113m),
        ("feta cheese", 150m),
        ("unsalted butter", 227m),
        ("butter", 227m),
        ("light mayo", 220m),
        ("mayo", 220m),
        ("light margarine", 227m),
        ("olive oil", 216m),
        ("sesame oil", 216m),
        ("vegetable oil", 218m),
        ("sugar free maple syrup", 322m),
        ("maple syrup", 322m),
        ("pb2 mixed with water", 240m),
        ("pb2", 96m),
        ("protein powder", 112m),
        ("quick oats", 80m),
        ("oats", 90m),
        ("dried lentils", 192m),
        ("dried barley", 200m),
        ("dried pasta", 100m),
        ("grilled chicken breast", 140m),
        ("grape tomatoes", 150m),
        ("strained tomatoes", 244m),
        ("roasted red bell pepper", 149m),
        ("red bell pepper", 149m),
        ("white mushrooms", 70m),
        ("zucchini", 124m),
        ("fresh basil", 16m),
        ("fresh cilantro", 16m),
        ("cilantro", 16m),
        ("egg white", 243m),
        ("hot sauce", 240m),
        ("salsa", 240m),
        ("soy sauce", 240m),
        ("water", 237m),
    };

    private static readonly HashSet<string> MassUnitGrams = new(StringComparer.OrdinalIgnoreCase) { "g", "gram", "grams" };
    private static readonly HashSet<string> MassUnitKilograms = new(StringComparer.OrdinalIgnoreCase) { "kg", "kilogram", "kilograms" };
    private static readonly HashSet<string> MassUnitOunces = new(StringComparer.OrdinalIgnoreCase) { "oz", "ounce", "ounces" };
    private static readonly HashSet<string> MassUnitPounds = new(StringComparer.OrdinalIgnoreCase) { "lb", "lbs", "pound", "pounds" };
    private static readonly HashSet<string> VolumeUnitCups = new(StringComparer.OrdinalIgnoreCase) { "cup", "cups" };
    private static readonly HashSet<string> VolumeUnitTablespoons = new(StringComparer.OrdinalIgnoreCase) { "tbsp", "tablespoon", "tablespoons" };
    private static readonly HashSet<string> VolumeUnitTeaspoons = new(StringComparer.OrdinalIgnoreCase) { "tsp", "teaspoon", "teaspoons" };
    private static readonly HashSet<string> VolumeUnitMillilitres = new(StringComparer.OrdinalIgnoreCase) { "ml", "milliliter", "milliliters", "millilitre", "millilitres" };
    private static readonly HashSet<string> VolumeUnitLitres = new(StringComparer.OrdinalIgnoreCase) { "l", "liter", "liters", "litre", "litres" };

    /// <summary>
    /// Converts <paramref name="quantity"/> <paramref name="unit"/> of <paramref name="name"/>
    /// to grams, picking an ingredient-appropriate density for volume units. Returns null when
    /// <paramref name="unit"/> isn't a recognized mass/volume unit - the caller should leave
    /// the ingredient as-is in that case, since it's already countable.
    /// </summary>
    public static (decimal Quantity, string Unit)? TryConvertToGrams(string name, decimal quantity, string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return null;
        }

        string trimmedUnit = unit.Trim();

        if (MassUnitGrams.Contains(trimmedUnit))
        {
            return (quantity, "g");
        }

        if (MassUnitKilograms.Contains(trimmedUnit))
        {
            return (quantity * 1000m, "g");
        }

        if (MassUnitOunces.Contains(trimmedUnit))
        {
            return (quantity * OuncesToGrams, "g");
        }

        if (MassUnitPounds.Contains(trimmedUnit))
        {
            return (quantity * PoundsToGrams, "g");
        }

        decimal? cups = trimmedUnit switch
        {
            _ when VolumeUnitCups.Contains(trimmedUnit) => quantity,
            _ when VolumeUnitTablespoons.Contains(trimmedUnit) => quantity / TablespoonsPerCup,
            _ when VolumeUnitTeaspoons.Contains(trimmedUnit) => quantity / TeaspoonsPerCup,
            _ when VolumeUnitMillilitres.Contains(trimmedUnit) => quantity / MillilitresPerCup,
            _ when VolumeUnitLitres.Contains(trimmedUnit) => quantity * 1000m / MillilitresPerCup,
            _ => null,
        };

        if (cups is null)
        {
            return null;
        }

        return (cups.Value * GramsPerCupFor(name), "g");
    }

    private static decimal GramsPerCupFor(string name)
    {
        string lowerName = name.Trim().ToLowerInvariant();

        foreach ((string keyword, decimal gramsPerCup) in DensitiesByKeyword.OrderByDescending(d => d.Keyword.Length))
        {
            if (lowerName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return gramsPerCup;
            }
        }

        return MillilitresPerCup;
    }
}
