using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.WeekPlan;

public interface IWeekPlanGeneratorService
{
    Task<(GenerateWeekPlanResult Result, SlotKey? MissingSlot, IReadOnlyList<WeekPlanEntryDto> Entries)> GenerateWeekAsync(
        GenerateWeekPlanRequest request,
        CancellationToken cancellationToken);
}
