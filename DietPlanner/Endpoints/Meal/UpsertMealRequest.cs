using DietPlanner.Endpoints.Slots;
using FluentValidation;

namespace DietPlanner.Endpoints.Meal;

public record UpsertMealRequest(
    Guid MealId,
    string Name,
    SlotKey SlotKey,
    int Kcal,
    int ProteinG,
    int CarbsG,
    int FibreG,
    int Plants,
    string? ZoeNotes,
    string MfName,
    string? Notes,
    IReadOnlyList<UpsertMealIngredientRequest> Ingredients)
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
            RuleFor(x => x.CarbsG).GreaterThanOrEqualTo(0);
            RuleFor(x => x.FibreG).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Plants).GreaterThanOrEqualTo(0);
            RuleFor(x => x.MfName).NotEmpty().WithMessage("MfName is required").MaximumLength(250);

            RuleForEach(x => x.Ingredients).ChildRules(ingredient =>
            {
                ingredient.RuleFor(i => i.Name).NotEmpty().WithMessage("Ingredient name is required").MaximumLength(200);
                ingredient.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Ingredient quantity must be greater than zero");
            });
        }
    }
}
