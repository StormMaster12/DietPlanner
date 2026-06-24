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
        List<MealEntry> meals = await _db.Meals
            .AsNoTracking()
            .Include(m => m.Ingredients)
            .ToListAsync(cancellationToken);

        return meals.Select(ToDto).ToList();
    }

    public async Task<MealDto?> GetMealAsync(Guid mealId, CancellationToken cancellationToken)
    {
        MealEntry? m = await _db.Meals
            .AsNoTracking()
            .Include(x => x.Ingredients)
            .SingleOrDefaultAsync(x => x.Id == mealId, cancellationToken);

        return m is null ? null : ToDto(m);
    }

    public async Task<(UpsertMealResult Result, MealDto? Meal)> UpsertMealAsync(UpsertMealRequest request, CancellationToken cancellationToken)
    {
        bool slotExists = await _db.Slots.AnyAsync(s => s.Key == request.SlotKey, cancellationToken);
        if (!slotExists)
        {
            return (UpsertMealResult.InvalidSlot, null);
        }

        var existing = await _db.Meals
            .Include(m => m.Ingredients)
            .SingleOrDefaultAsync(m => m.Id == request.MealId, cancellationToken);

        List<MealIngredient> newIngredients = request.Ingredients
            .Select(i => new MealIngredient
            {
                Id = Guid.NewGuid(),
                MealId = request.MealId,
                Name = i.Name.Trim(),
                Quantity = i.Quantity,
                Unit = string.IsNullOrWhiteSpace(i.Unit) ? null : i.Unit.Trim()
            })
            .ToList();

        if (existing is null)
        {
            existing = new MealEntry
            {
                Id = request.MealId,
                Name = request.Name.Trim(),
                SlotKey = request.SlotKey,
                Kcal = request.Kcal,
                ProteinG = request.ProteinG,
                CarbsG = request.CarbsG,
                FibreG = request.FibreG,
                Plants = request.Plants,
                ZoeNotes = request.ZoeNotes,
                MfName = request.MfName.Trim(),
                Notes = request.Notes,
                Ingredients = newIngredients
            };
            _db.Meals.Add(existing);
        }
        else
        {
            existing.Name = request.Name.Trim();
            existing.SlotKey = request.SlotKey;
            existing.Kcal = request.Kcal;
            existing.ProteinG = request.ProteinG;
            existing.CarbsG = request.CarbsG;
            existing.FibreG = request.FibreG;
            existing.Plants = request.Plants;
            existing.ZoeNotes = request.ZoeNotes;
            existing.MfName = request.MfName.Trim();
            existing.Notes = request.Notes;

            // Replace the ingredient list wholesale rather than diffing individual rows - the
            // meal form always submits the complete current set of ingredients. Clearing the
            // tracked collection lets EF's orphan-deletion handle the removals. The new rows are
            // added to the DbSet explicitly (not just the navigation collection) because EF's
            // change detector otherwise mistakes their client-set, store-generated-looking Guid
            // keys for existing rows and marks them Modified instead of Added, which then fails
            // as a no-op update.
            existing.Ingredients.Clear();
            _db.MealIngredients.AddRange(newIngredients);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return (UpsertMealResult.Success, ToDto(existing));
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

    private static MealDto ToDto(MealEntry m)
    {
        IReadOnlyList<MealIngredientDto> ingredients = m.Ingredients
            .Select(i => new MealIngredientDto(i.Id, i.Name, i.Quantity, i.Unit))
            .ToList();

        return new MealDto(m.Id, m.Name, m.SlotKey, m.Kcal, m.ProteinG, m.CarbsG, m.FibreG, m.Plants, m.ZoeNotes, m.MfName, m.Notes, ingredients);
    }
}