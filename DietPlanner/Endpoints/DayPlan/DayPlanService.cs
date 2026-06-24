using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.DayPlan;

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

        List<WeekPlan.WeekPlanEntry> planned = await _db.WeekPlanEntries
            .AsNoTracking()
            .Where(x => x.Date == date)
            .Include(x => x.Meal)
            .ThenInclude(m => m.Ingredients)
            .Include(x => x.Slot)
            .ToListAsync(cancellationToken);

        static int Scale(int v, decimal mult)
        {
            return (int)Math.Round(v * (double)mult);
        }

        List<DayPlanMealDto> items = planned
            .Select(p =>
            {
                decimal mult = p.PortionMultiplier;
                MealEntry m = p.Meal;

                List<DayPlanIngredientDto> ingredients = m.Ingredients
                    .Select(i => new DayPlanIngredientDto(i.Name, Math.Round(i.Quantity * mult, 2), i.Unit))
                    .ToList();

                return new DayPlanMealDto(
                    date,
                    p.SlotKey,
                    p.Slot.SortOrder,
                    p.MealId,
                    m.Name,
                    m.MfName,
                    mult,
                    Scale(m.Kcal, mult),
                    Scale(m.ProteinG, mult),
                    Scale(m.CarbsG, mult),
                    Scale(m.FibreG, mult),
                    Scale(m.Plants, mult),
                    m.ZoeNotes,
                    p.Notes,
                    ingredients
                );
            })
            .OrderBy(x => x.SlotOrder)
            .ToList();

        DayPlanTotalsDto totals = new(
            items.Sum(i => i.Kcal),
            items.Sum(i => i.ProteinG),
            items.Sum(i => i.CarbsG),
            items.Sum(i => i.FibreG),
            items.Sum(i => i.Plants));

        DayPlanTotalsDto remaining = new(
            targets.DailyKcalTarget - totals.Kcal,
            targets.DailyProteinTargetG - totals.ProteinG,
            targets.DailyCarbTargetG - totals.CarbsG,
            targets.DailyFibreTargetG - totals.FibreG,
            targets.DailyPlantsTarget - totals.Plants);

        return new DayPlanResponseDto(date, items, totals, targets, remaining);
    }
}
