using FluentValidation;

namespace DietPlanner.Endpoints.Settings;

public record UpdateSettingsRequest(
    int DailyKcalTarget,
    int DailyProteinTargetG,
    int DailyFibreTargetG,
    int DailyPlantsTarget
)
{
    public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
    {
        public UpdateSettingsRequestValidator()
        {
            _ = RuleFor(x => x.DailyKcalTarget).InclusiveBetween(1200, 5000);
            _ = RuleFor(x => x.DailyProteinTargetG).InclusiveBetween(50, 350);
            _ = RuleFor(x => x.DailyFibreTargetG).InclusiveBetween(10, 80);
            _ = RuleFor(x => x.DailyPlantsTarget).InclusiveBetween(5, 80);
        }
    }
}
