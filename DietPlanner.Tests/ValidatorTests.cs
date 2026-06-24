using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace DietPlanner.Tests;

public sealed class ValidatorTests
{
    [Theory]
    [InlineData(1199)]
    [InlineData(5001)]
    public void UpdateSettingsRequestValidator_RejectsKcalOutsideRange(int kcal)
    {
        UpdateSettingsRequest.UpdateSettingsRequestValidator validator = new();

        ValidationResult result = validator.Validate(new UpdateSettingsRequest(kcal, 150, 200, 25, 20));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateSettingsRequestValidator_AcceptsValuesWithinRange()
    {
        UpdateSettingsRequest.UpdateSettingsRequestValidator validator = new();

        ValidationResult result = validator.Validate(new UpdateSettingsRequest(2300, 165, 220, 30, 30));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateSlotRequestValidator_RejectsEmptyDisplayName()
    {
        CreateSlotRequest.CreateSlotRequestValidator validator = new();

        ValidationResult result = validator.Validate(new CreateSlotRequest(SlotKey.Breakfast, "", 1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "DisplayName is required");
    }

    [Fact]
    public void CreateSlotRequestValidator_RejectsDisplayNameOver100Characters()
    {
        CreateSlotRequest.CreateSlotRequestValidator validator = new();

        ValidationResult result = validator.Validate(new CreateSlotRequest(SlotKey.Breakfast, new string('a', 101), 1));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateSlotRequestValidator_RejectsNegativeSortOrder()
    {
        CreateSlotRequest.CreateSlotRequestValidator validator = new();

        ValidationResult result = validator.Validate(new CreateSlotRequest(SlotKey.Breakfast, "Breakfast", -1));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateSlotRequestValidator_AcceptsValidRequest()
    {
        CreateSlotRequest.CreateSlotRequestValidator validator = new();

        ValidationResult result = validator.Validate(new CreateSlotRequest(SlotKey.Breakfast, "Breakfast", 0));

        result.IsValid.Should().BeTrue();
    }

    private static UpsertMealRequest ValidMealRequest() => new(
        Guid.NewGuid(),
        "Oatmeal",
        SlotKey.Breakfast,
        300, 10, 40, 5, 2,
        "Good fibre",
        "Oatmeal MFP",
        null,
        new List<UpsertMealIngredientRequest> { new("Oats", 80, "g") });

    [Fact]
    public void UpsertMealRequestValidator_AcceptsValidRequest()
    {
        UpsertMealRequest.UpsertMealRequestValidator validator = new();

        ValidationResult result = validator.Validate(ValidMealRequest());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpsertMealRequestValidator_RejectsEmptyMealId()
    {
        UpsertMealRequest.UpsertMealRequestValidator validator = new();

        ValidationResult result = validator.Validate(ValidMealRequest() with { MealId = Guid.Empty });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpsertMealRequestValidator_RejectsZeroOrNegativeKcal()
    {
        UpsertMealRequest.UpsertMealRequestValidator validator = new();

        ValidationResult result = validator.Validate(ValidMealRequest() with { Kcal = 0 });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpsertMealRequestValidator_RejectsBlankIngredientName()
    {
        UpsertMealRequest.UpsertMealRequestValidator validator = new();

        ValidationResult result = validator.Validate(ValidMealRequest() with
        {
            Ingredients = new List<UpsertMealIngredientRequest> { new("", 80, "g") }
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpsertMealRequestValidator_RejectsZeroIngredientQuantity()
    {
        UpsertMealRequest.UpsertMealRequestValidator validator = new();

        ValidationResult result = validator.Validate(ValidMealRequest() with
        {
            Ingredients = new List<UpsertMealIngredientRequest> { new("Oats", 0, "g") }
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpsertWeekEntryRequestValidator_RejectsZeroPortionMultiplier()
    {
        UpsertWeekEntryRequest.UpsertWeekEntryRequestValidator validator = new();

        ValidationResult result = validator.Validate(new UpsertWeekEntryRequest(new DateOnly(2026, 1, 5), SlotKey.Breakfast, Guid.NewGuid(), 0m, null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpsertWeekEntryRequestValidator_AcceptsValidRequest()
    {
        UpsertWeekEntryRequest.UpsertWeekEntryRequestValidator validator = new();

        ValidationResult result = validator.Validate(new UpsertWeekEntryRequest(new DateOnly(2026, 1, 5), SlotKey.Breakfast, Guid.NewGuid(), 1m, null));

        result.IsValid.Should().BeTrue();
    }
}
