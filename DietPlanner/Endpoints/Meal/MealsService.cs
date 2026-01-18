using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Meal;

public sealed class MealsService : IMealsService
{
    private readonly AppDbContext _db;

    public MealsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MealDto>> GetMealsAsync(CancellationToken cancellationToken)
    {
        return await _db.Meals
            .AsNoTracking()
            .Select(m => new MealDto(m.Id, m.Name, m.SlotKey, m.Kcal, m.ProteinG, m.FibreG, m.Plants, m.ZoeNotes, m.MfName, m.Notes))
            .ToListAsync(cancellationToken);
    }

    public async Task<MealDto?> GetMealAsync(Guid mealId, CancellationToken cancellationToken)
    {
        var m = await _db.Meals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mealId, cancellationToken);

        return m is null ? null : new MealDto(m.Id, m.Name, m.SlotKey, m.Kcal, m.ProteinG, m.FibreG, m.Plants, m.ZoeNotes, m.MfName, m.Notes);
    }

    public async Task<(UpsertMealResult Result, MealDto? Meal)> UpsertMealAsync(UpsertMealRequest request, CancellationToken cancellationToken)
    {
        bool slotExists = await _db.Slots.AnyAsync(s => s.Key == request.SlotKey, cancellationToken);
        if (!slotExists)
        {
            return (UpsertMealResult.InvalidSlot, null);
        }

        var existing = await _db.Meals.SingleOrDefaultAsync(m => m.Id == request.MealId, cancellationToken);

        if (existing is null)
        {
            existing = new MealEntry
            {
                Id = request.MealId,
                Name = request.Name.Trim(),
                SlotKey = request.SlotKey,
                Kcal = request.Kcal,
                ProteinG = request.ProteinG,
                FibreG = request.FibreG,
                Plants = request.Plants,
                ZoeNotes = request.ZoeNotes,
                MfName = request.MfName.Trim(),
                Notes = request.Notes
            };
            _db.Meals.Add(existing);
        }
        else
        {
            existing.Name = request.Name.Trim();
            existing.SlotKey = request.SlotKey;
            existing.Kcal = request.Kcal;
            existing.ProteinG = request.ProteinG;
            existing.FibreG = request.FibreG;
            existing.Plants = request.Plants;
            existing.ZoeNotes = request.ZoeNotes;
            existing.MfName = request.MfName.Trim();
            existing.Notes = request.Notes;
        }

        await _db.SaveChangesAsync(cancellationToken);

        MealDto dto = new(existing.Id, existing.Name, existing.SlotKey, existing.Kcal, existing.ProteinG, existing.FibreG, existing.Plants, existing.ZoeNotes, existing.MfName, existing.Notes);
        return (UpsertMealResult.Success, dto);
    }

    public async Task<DeleteResult> DeleteMealAsync(Guid mealId, CancellationToken cancellationToken)
    {
        MealEntry? entity = await _db.Meals.SingleOrDefaultAsync(m => m.Id == mealId, cancellationToken);
        if (entity is null)
        {
            return DeleteResult.NotFound;
        }

        _db.Meals.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return DeleteResult.Success;
    }
}