using DietPlanner.Endpoints.Slots;
using FluentValidation;

namespace DietPlanner.Endpoints.WeekPlan;

public record UpsertWeekEntryRequest(
    DateOnly Date,
    SlotKey SlotKey,
    Guid MealId,
    decimal PortionMultiplier,
    string? Notes)
{
    public class UpsertWeekEntryRequestValidator : AbstractValidator<UpsertWeekEntryRequest>
    {
        public UpsertWeekEntryRequestValidator()
        {
            RuleFor(x => x.Date).NotEmpty();
            RuleFor(x => x.SlotKey).NotEmpty().WithMessage("SlotKey is required");
            RuleFor(x => x.MealId).NotEmpty().WithMessage("MealId is required");
            RuleFor(x => x.PortionMultiplier).GreaterThan(0).WithMessage("PortionMultiplier must be > 0");
        }
    }
}
