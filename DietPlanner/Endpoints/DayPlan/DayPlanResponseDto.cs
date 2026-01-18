namespace DietPlanner.Endpoints.DayPlan;

public sealed record DayPlanResponseDto(
    DateOnly Date,
    IReadOnlyList<DayPlanMealDto> Meals,
    DayPlanTotalsDto Totals,
    Settings.SettingsDto Targets,
    DayPlanTotalsDto Remaining);
