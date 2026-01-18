using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Day;

public sealed record DayPlanMealDto(
    DateOnly Date,
    SlotKey SlotKey,
    int SlotOrder,
    MealId MealId,
    string Name,
    string MfName,
    decimal PortionMultiplier,
    int Kcal,
    int ProteinG,
    int FibreG,
    int Plants,
    string? ZoeNotes,
    string? Notes);

public sealed record DayPlanTotalsDto(int Kcal, int ProteinG, int FibreG, int Plants);

public sealed record DayPlanResponseDto(
    DateOnly Date,
    IReadOnlyList<DayPlanMealDto> Meals,
    DayPlanTotalsDto Totals,
    Settings.SettingsDto Targets,
    DayPlanTotalsDto Remaining);


public interface IDayPlanService
{
    Task<DayPlanResponseDto> GetDayPlanAsync(DateOnly date, CancellationToken cancellationToken);
}

public sealed class DayPlanService : IDayPlanService
{
    private readonly AppDbContext _db;
    private readonly ISettingsService _settings;

    public DayPlanService(AppDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<DayPlanResponseDto> GetDayPlanAsync(DateOnly date, CancellationToken cancellationToken)
    {
        SettingsDto targets = await _settings.GetAsync(cancellationToken);

        Dictionary<SlotKey, int> slots = await _db.Slots
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ToDictionaryAsync(s => s.Key, s => s.SortOrder, cancellationToken);

        List<WeekPlan.WeekPlanEntry> planned = await _db.WeekPlanEntries
            .AsNoTracking()
            .Where(x => x.Date == date)
            .Include(x => x.Meal)
            .ToListAsync(cancellationToken);

        static int Scale(int v, decimal mult)
        {
            return (int)Math.Round(v * (double)mult);
        }

        List<DayPlanMealDto> items = planned
            .Select(p =>
            {
                decimal mult = p.PortionMultiplier;
                int slotOrder = slots.TryGetValue(p.SlotKey, out int so) ? so : 999;
                MealEntry m = p.Meal;

                return new DayPlanMealDto(
                    date,
                    p.SlotKey,
                    slotOrder,
                    p.MealId,
                    m.Name,
                    m.MfName,
                    mult,
                    Scale(m.Kcal, mult),
                    Scale(m.ProteinG, mult),
                    Scale(m.FibreG, mult),
                    Scale(m.Plants, mult),
                    m.ZoeNotes,
                    p.Notes
                );
            })
            .OrderBy(x => x.SlotOrder)
            .ToList();

        DayPlanTotalsDto totals = new(
            items.Sum(i => i.Kcal),
            items.Sum(i => i.ProteinG),
            items.Sum(i => i.FibreG),
            items.Sum(i => i.Plants));

        DayPlanTotalsDto remaining = new(
            targets.DailyKcalTarget - totals.Kcal,
            targets.DailyProteinTargetG - totals.ProteinG,
            targets.DailyFibreTargetG - totals.FibreG,
            targets.DailyPlantsTarget - totals.Plants);

        return new DayPlanResponseDto(date, items, totals, targets, remaining);
    }
}
