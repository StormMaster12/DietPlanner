using DietPlanner.Endpoints.DayPlan;

namespace DietPlanner.Tests;

[TestFixture]
public sealed class IngredientUnitConverterTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void TryConvertToGrams_ReturnsNull_ForNoUnit(string? unit)
    {
        Assert.That(IngredientUnitConverter.TryConvertToGrams("chicken breast", 4m, unit), Is.Null);
    }

    [TestCase("clove")]
    [TestCase("leaf")]
    [TestCase("slice")]
    public void TryConvertToGrams_ReturnsNull_ForUnrecognizedUnit(string unit)
    {
        Assert.That(IngredientUnitConverter.TryConvertToGrams("garlic", 2m, unit), Is.Null);
    }

    [Test]
    public void TryConvertToGrams_PassesThroughGrams()
    {
        var result = IngredientUnitConverter.TryConvertToGrams("baby spinach", 600m, "g");

        Assert.That(result!.Value.Quantity, Is.EqualTo(600m));
        Assert.That(result!.Value.Unit, Is.EqualTo("g"));
    }

    [Test]
    public void TryConvertToGrams_ConvertsKilograms()
    {
        var result = IngredientUnitConverter.TryConvertToGrams("flour", 1.5m, "kg");

        Assert.That(result!.Value.Quantity, Is.EqualTo(1500m));
    }

    [TestCase("oz", 16.00, 453.592)]
    [TestCase("lb", 3.00, 1360.776)]
    [TestCase("lbs", 3.00, 1360.776)]
    public void TryConvertToGrams_ConvertsImperialMassUnits(string unit, decimal quantity, decimal expectedGrams)
    {
        var result = IngredientUnitConverter.TryConvertToGrams("chicken breast", quantity, unit);

        Assert.That(result!.Value.Quantity, Is.EqualTo(expectedGrams).Within(0.01m));
    }

    [Test]
    public void TryConvertToGrams_ConvertsCupsAndTablespoonsToTheSameUnit_ForTheSameIngredient()
    {
        var fromCups = IngredientUnitConverter.TryConvertToGrams("0% fat Greek yogurt", 3.50m, "cup");
        var fromTablespoons = IngredientUnitConverter.TryConvertToGrams("0% fat Greek yogurt", 12.00m, "tbsp");

        Assert.That(fromCups!.Value.Unit, Is.EqualTo("g"));
        Assert.That(fromTablespoons!.Value.Unit, Is.EqualTo("g"));

        // 3.5 cups of Greek yogurt should be roughly 4.67x heavier than 12 tbsp (= 0.75 cup).
        decimal ratio = fromCups.Value.Quantity / fromTablespoons.Value.Quantity;
        Assert.That(ratio, Is.EqualTo(3.50m / 0.75m).Within(0.001m));
    }

    [Test]
    public void TryConvertToGrams_TreatsPluralCupUnitTheSameAsSingular()
    {
        var fromCup = IngredientUnitConverter.TryConvertToGrams("water", 10.00m, "cup");
        var fromCups = IngredientUnitConverter.TryConvertToGrams("water", 6.00m, "cups");

        Assert.That(fromCup!.Value.Unit, Is.EqualTo(fromCups!.Value.Unit));
        Assert.That(fromCup.Value.Quantity / 10m, Is.EqualTo(fromCups.Value.Quantity / 6m).Within(0.001m));
    }

    [Test]
    public void TryConvertToGrams_UsesIngredientSpecificDensity_NotJustWater()
    {
        var flour = IngredientUnitConverter.TryConvertToGrams("all-purpose flour", 1m, "cup");
        var water = IngredientUnitConverter.TryConvertToGrams("water", 1m, "cup");

        Assert.That(flour!.Value.Quantity, Is.Not.EqualTo(water!.Value.Quantity));
    }

    [Test]
    public void TryConvertToGrams_ConvertsTeaspoonsAndTablespoonsToTheSameUnit_ForTheSameIngredient()
    {
        var fromTeaspoons = IngredientUnitConverter.TryConvertToGrams("onion powder", 1.00m, "tsp");
        var fromTablespoons = IngredientUnitConverter.TryConvertToGrams("onion powder", 4.50m, "tbsp");

        Assert.That(fromTeaspoons!.Value.Unit, Is.EqualTo("g"));
        // 1 tbsp == 3 tsp, so 4.5 tbsp == 13.5 tsp.
        Assert.That(fromTablespoons!.Value.Quantity / fromTeaspoons.Value.Quantity, Is.EqualTo(13.5m).Within(0.001m));
    }

    [Test]
    public void TryConvertToGrams_ConvertsMillilitresAndLitres()
    {
        var fromMl = IngredientUnitConverter.TryConvertToGrams("strained tomatoes", 975m, "ml");
        var fromL = IngredientUnitConverter.TryConvertToGrams("strained tomatoes", 0.975m, "l");

        Assert.That(fromMl!.Value.Quantity, Is.EqualTo(fromL!.Value.Quantity).Within(0.001m));
    }
}
