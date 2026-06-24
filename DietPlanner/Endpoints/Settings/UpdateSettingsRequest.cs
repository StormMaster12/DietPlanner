using FluentValidation;

namespace DietPlanner.Endpoints.Settings;

public record UpdateSettingsRequest(
    int DailyKcalTarget,
    int DailyProteinTargetG,
    int DailyCarbTargetG,
    int DailyFibreTargetG,
    int DailyPlantsTarget
)
{
    public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
    {
        public UpdateSettingsRequestValidator()
        {
            RuleFor(x => x.DailyKcalTarget).InclusiveBetween(1200, 5000);
            RuleFor(x => x.DailyProteinTargetG).InclusiveBetween(50, 350);
            RuleFor(x => x.DailyCarbTargetG).InclusiveBetween(20, 600);
            RuleFor(x => x.DailyFibreTargetG).InclusiveBetween(10, 80);
            RuleFor(x => x.DailyPlantsTarget).InclusiveBetween(5, 80);
        }
    }
}
