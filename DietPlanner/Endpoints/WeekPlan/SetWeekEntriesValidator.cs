using FluentValidation;

namespace DietPlanner.Endpoints.WeekPlan;

/// <summary>Validates the whole batch sent to <c>PUT /api/week/set</c>, including each entry.</summary>
public sealed class SetWeekEntriesValidator : AbstractValidator<IReadOnlyList<UpsertWeekEntryRequest>>
{
    public const int MinEntries = 1;

    // A full week of the four standard daily slots.
    public const int MaxEntries = 28;

    public SetWeekEntriesValidator()
    {
        RuleFor(entries => entries.Count)
            .InclusiveBetween(MinEntries, MaxEntries)
            .OverridePropertyName("entries")
            .WithMessage($"entries must be between {MinEntries} and {MaxEntries} items");

        RuleForEach(entries => entries)
            .SetValidator(new UpsertWeekEntryRequest.UpsertWeekEntryRequestValidator())
            .OverridePropertyName("entries");
    }
}
