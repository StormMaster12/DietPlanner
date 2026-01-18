using DietPlanner.Endpoints.Slots;
using FluentValidation;

namespace DietPlanner.Endpoints.Meal;

public record UpsertMealRequest(
    Guid MealId,
    string Name,
    SlotKey SlotKey,
    int Kcal,
    int ProteinG,
    int FibreG,
    int Plants,
    string? ZoeNotes,
    string MfName,
    string? Notes)
{
    public class UpsertMealRequestValidator : AbstractValidator<UpsertMealRequest>
    {
        public UpsertMealRequestValidator()
        {
            RuleFor(x => x.MealId).NotEmpty().WithMessage("MealId is required");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(200);
            RuleFor(x => x.SlotKey).NotEmpty().WithMessage("SlotKey is required");
            RuleFor(x => x.Kcal).GreaterThan(0);
            RuleFor(x => x.ProteinG).GreaterThanOrEqualTo(0);
            RuleFor(x => x.FibreG).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Plants).GreaterThanOrEqualTo(0);
            RuleFor(x => x.MfName).NotEmpty().WithMessage("MfName is required").MaximumLength(250);
        }
    }
}
