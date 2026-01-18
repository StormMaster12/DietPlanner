namespace DietPlanner.Endpoints.DayPlan;

public interface IDayPlanService
{
    Task<DayPlanResponseDto> GetDayPlanAsync(DateOnly date, CancellationToken cancellationToken);
}
