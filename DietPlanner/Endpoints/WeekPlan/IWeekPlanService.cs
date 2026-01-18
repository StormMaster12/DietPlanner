using DietPlanner.Endpoints.Slots;

namespace DietPlanner.Endpoints.WeekPlan;

public interface IWeekPlanService
{
    Task<IReadOnlyList<WeekPlanEntryDto>> GetWeekAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken);
    Task<(UpsertWeekEntryResult Result, WeekPlanEntryDto? Entry)> UpsertEntryAsync(UpsertWeekEntryRequest request, CancellationToken cancellationToken);
    Task<(UpsertWeekEntryResult Result, int Count)> SetWeekAsync(IReadOnlyList<UpsertWeekEntryRequest> entries, CancellationToken cancellationToken);
    Task<DeleteResult> DeleteEntryAsync(DateOnly date, SlotKey slotKey, CancellationToken cancellationToken);
}
