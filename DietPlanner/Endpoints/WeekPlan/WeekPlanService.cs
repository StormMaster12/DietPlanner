using DietPlanner.Endpoints.Slots;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.WeekPlan;

public sealed class WeekPlanService : IWeekPlanService
{
    private readonly AppDbContext _db;

    public WeekPlanService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WeekPlanEntryDto>> GetWeekAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        return await _db.WeekPlanEntries
            .AsNoTracking()
            .Where(x => x.Date >= start && x.Date <= end)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.SlotKey)
            .Select(x => new WeekPlanEntryDto(x.Date, x.SlotKey, x.MealId, x.PortionMultiplier, x.Notes))
            .ToListAsync(cancellationToken);
    }

    public async Task<(UpsertWeekEntryResult Result, WeekPlanEntryDto? Entry)> UpsertEntryAsync(UpsertWeekEntryRequest request, CancellationToken cancellationToken)
    {
        if (!await _db.Slots.AnyAsync(s => s.Key == request.SlotKey, cancellationToken))
        {
            return (UpsertWeekEntryResult.InvalidSlot, null);
        }

        if (!await _db.Meals.AnyAsync(m => m.Id == request.MealId, cancellationToken))
        {
            return (UpsertWeekEntryResult.UnknownMeal, null);
        }

        WeekPlanEntry? existing = await _db.WeekPlanEntries
            .SingleOrDefaultAsync(x => x.Date == request.Date && x.SlotKey == request.SlotKey, cancellationToken);

        if (existing is null)
        {
            existing = new WeekPlanEntry(Guid.NewGuid(), request.Date, request.SlotKey, request.MealId, request.PortionMultiplier, request.Notes);
            _db.WeekPlanEntries.Add(existing);
        }
        else
        {
            existing = existing with { MealId = request.MealId, PortionMultiplier = request.PortionMultiplier, Notes = request.Notes };
        }

        await _db.SaveChangesAsync(cancellationToken);

        return (UpsertWeekEntryResult.Success,
            new WeekPlanEntryDto(existing.Date, existing.SlotKey, existing.MealId, existing.PortionMultiplier, existing.Notes));
    }

    public async Task<(UpsertWeekEntryResult Result, int Count)> SetWeekAsync(IReadOnlyList<UpsertWeekEntryRequest> entries, CancellationToken cancellationToken)
    {
        if (entries.Count is < 1 or > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(entries));
        }

        // Validate sets once
        HashSet<SlotKey> slotSet = (await _db.Slots.AsNoTracking().Select(s => s.Key).ToListAsync(cancellationToken)).ToHashSet();
        HashSet<Guid> mealSet = (await _db.Meals.AsNoTracking().Select(m => m.Id).ToListAsync(cancellationToken)).ToHashSet();

        foreach (UpsertWeekEntryRequest e in entries)
        {
            if (!slotSet.Contains(e.SlotKey))
            {
                return (UpsertWeekEntryResult.InvalidSlot, 0);
            }

            if (!mealSet.Contains(e.MealId))
            {
                return (UpsertWeekEntryResult.UnknownMeal, 0);
            }

            WeekPlanEntry? existing = await _db.WeekPlanEntries
                .SingleOrDefaultAsync(x => x.Date == e.Date && x.SlotKey == e.SlotKey, cancellationToken);

            if (existing is null)
            {
                _db.WeekPlanEntries.Add(new WeekPlanEntry(Guid.NewGuid(), e.Date, e.SlotKey, e.MealId, e.PortionMultiplier, e.Notes));
            }
            else
            {
                existing = existing with
                {
                    MealId = e.MealId,
                    PortionMultiplier = e.PortionMultiplier,
                    Notes = e.Notes
                };
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return (UpsertWeekEntryResult.Success, entries.Count);
    }

    public async Task<DeleteResult> DeleteEntryAsync(DateOnly date, SlotKey slotKey, CancellationToken cancellationToken)
    {
        WeekPlanEntry? existing = await _db.WeekPlanEntries
            .SingleOrDefaultAsync(x => x.Date == date && x.SlotKey == slotKey, cancellationToken);

        if (existing is null)
        {
            return DeleteResult.NotFound;
        }

        _db.WeekPlanEntries.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return DeleteResult.Success;
    }
}