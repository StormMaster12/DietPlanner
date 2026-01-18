using FluentValidation;

namespace DietPlanner.Endpoints.Slots;

public record CreateSlotRequest(
    SlotKey Key,
    string DisplayName,
    int SortOrder)
{
    public class CreateSlotRequestValidator : AbstractValidator<CreateSlotRequest>
    {
        public CreateSlotRequestValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().WithMessage("Key is required");
            RuleFor(x => x.DisplayName)
                .NotEmpty().WithMessage("DisplayName is required")
                .MaximumLength(100).WithMessage("DisplayName must be at most 100 characters long");
            RuleFor(x => x.SortOrder)
                .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be non-negative");
        }
    };
}
