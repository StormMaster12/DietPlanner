using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.WeekPlan;

/// <summary>
/// Randomly assigns one meal to every (day, slot) cell of a seven day week, biased towards
/// reusing a small handful of meals per slot ("cook once, eat two or three times") while still
/// trying to keep each day's total carbohydrates at or under the daily target. Protein is allowed
/// to exceed its target freely - only a protein shortfall counts against a candidate plan.
/// </summary>
public sealed class WeekPlanGeneratorService : IWeekPlanGeneratorService
{
    private const int DaysInWeek = 7;

    /// <summary>
    /// At most this many distinct meals are used for a single slot across the whole week, so that
    /// each chosen meal is realistically cooked once and then eaten again on the days it repeats.
    /// </summary>
    private const int MaxDistinctMealsPerSlotPerWeek = 3;

    /// <summary>
    /// Number of full week assignments to try at random before keeping the best one found. Higher
    /// values search harder for a combination that keeps every day's carbs at or under target.
    /// </summary>
    private const int RandomAttemptCount = 200;

    private readonly AppDbContext _db;
    private readonly ISettingsService _settings;

    public WeekPlanGeneratorService(AppDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<(GenerateWeekPlanResult Result, SlotKey? MissingSlot, IReadOnlyList<WeekPlanEntryDto> Entries)> GenerateWeekAsync(
        GenerateWeekPlanRequest request,
        CancellationToken cancellationToken)
    {
        SettingsDto targets = await _settings.GetAsync(cancellationToken);

        SlotKey[] slotOrder = Enum.GetValues<SlotKey>();

        // Every slot needs at least one candidate meal or there is nothing to randomly assign to it.
        var mealPoolBySlot = new Dictionary<SlotKey, List<MealEntry>>();
        foreach (SlotKey slot in slotOrder)
        {
            List<MealEntry> mealsForSlot = await _db.Meals
                .AsNoTracking()
                .Where(m => m.SlotKey == slot)
                .ToListAsync(cancellationToken);

            if (mealsForSlot.Count == 0)
            {
                return (GenerateWeekPlanResult.NoMealsAvailableForSlot, slot, Array.Empty<WeekPlanEntryDto>());
            }

            mealPoolBySlot[slot] = mealsForSlot;
        }

        MealEntry[,] bestAssignment = BuildRandomWeekAssignment(slotOrder, mealPoolBySlot);
        long bestScore = ScoreWeekAssignment(bestAssignment, slotOrder.Length, targets);

        for (int attempt = 1; attempt < RandomAttemptCount && bestScore > 0; attempt++)
        {
            MealEntry[,] candidateAssignment = BuildRandomWeekAssignment(slotOrder, mealPoolBySlot);
            long candidateScore = ScoreWeekAssignment(candidateAssignment, slotOrder.Length, targets);

            if (candidateScore < bestScore)
            {
                bestScore = candidateScore;
                bestAssignment = candidateAssignment;
            }
        }

        IReadOnlyList<WeekPlanEntryDto> savedEntries = await PersistWeekAssignmentAsync(
            request.WeekStartDate, slotOrder, bestAssignment, cancellationToken);

        return (GenerateWeekPlanResult.Success, null, savedEntries);
    }

    /// <summary>
    /// Builds one full random candidate for the week: for every slot, picks a small rotation of
    /// meals and lays them across the seven days in contiguous "cook once, eat several times" blocks.
    /// </summary>
    private static MealEntry[,] BuildRandomWeekAssignment(SlotKey[] slotOrder, Dictionary<SlotKey, List<MealEntry>> mealPoolBySlot)
    {
        var assignment = new MealEntry[DaysInWeek, slotOrder.Length];

        for (int slotIndex = 0; slotIndex < slotOrder.Length; slotIndex++)
        {
            MealEntry[] mealForEachDay = BuildWeeklyRotationForSlot(mealPoolBySlot[slotOrder[slotIndex]]);

            for (int dayIndex = 0; dayIndex < DaysInWeek; dayIndex++)
            {
                assignment[dayIndex, slotIndex] = mealForEachDay[dayIndex];
            }
        }

        return assignment;
    }

    /// <summary>
    /// Picks up to <see cref="MaxDistinctMealsPerSlotPerWeek"/> meals from the pool and repeats each
    /// of them across a contiguous block of days so the seven days are fully covered.
    /// </summary>
    private static MealEntry[] BuildWeeklyRotationForSlot(List<MealEntry> mealPoolForSlot)
    {
        int distinctMealCount = Math.Min(MaxDistinctMealsPerSlotPerWeek, mealPoolForSlot.Count);

        List<MealEntry> chosenMeals = mealPoolForSlot
            .OrderBy(_ => Random.Shared.Next())
            .Take(distinctMealCount)
            .ToList();

        int[] daysPerMeal = SplitDaysAsEvenlyAsPossible(DaysInWeek, distinctMealCount);

        List<List<MealEntry>> repeatBlocks = new(distinctMealCount);
        for (int i = 0; i < distinctMealCount; i++)
        {
            repeatBlocks.Add(Enumerable.Repeat(chosenMeals[i], daysPerMeal[i]).ToList());
        }

        // Shuffle which block of days each meal occupies, but keep every meal's own days contiguous
        // so it reads like a realistic meal-prep rotation (e.g. "meal A: Mon-Wed, meal B: Thu-Fri").
        return repeatBlocks
            .OrderBy(_ => Random.Shared.Next())
            .SelectMany(block => block)
            .ToArray();
    }

    /// <summary>
    /// Splits <paramref name="totalDays"/> across <paramref name="groupCount"/> groups as evenly as
    /// possible, e.g. 7 days across 3 groups becomes [3, 2, 2].
    /// </summary>
    private static int[] SplitDaysAsEvenlyAsPossible(int totalDays, int groupCount)
    {
        int[] daysPerGroup = new int[groupCount];
        int baseDaysPerGroup = totalDays / groupCount;
        int extraDaysToDistribute = totalDays % groupCount;

        for (int i = 0; i < groupCount; i++)
        {
            daysPerGroup[i] = baseDaysPerGroup + (i < extraDaysToDistribute ? 1 : 0);
        }

        return daysPerGroup;
    }

    /// <summary>
    /// Scores a candidate week: every gram of carbs over the daily target is penalised heavily,
    /// since carbs must be matched or kept under target. Falling short of the protein target is
    /// penalised lightly, since extra protein above target is always acceptable.
    /// </summary>
    private static long ScoreWeekAssignment(MealEntry[,] assignment, int slotCount, SettingsDto targets)
    {
        const long CarbOverageWeight = 1000;

        long totalScore = 0;

        for (int dayIndex = 0; dayIndex < DaysInWeek; dayIndex++)
        {
            int dailyCarbsG = 0;
            int dailyProteinG = 0;

            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                MealEntry meal = assignment[dayIndex, slotIndex];
                dailyCarbsG += meal.CarbsG;
                dailyProteinG += meal.ProteinG;
            }

            int carbOverageG = Math.Max(0, dailyCarbsG - targets.DailyCarbTargetG);
            int proteinShortfallG = Math.Max(0, targets.DailyProteinTargetG - dailyProteinG);

            totalScore += carbOverageG * CarbOverageWeight + proteinShortfallG;
        }

        return totalScore;
    }

    /// <summary>
    /// Replaces any existing week plan entries for the target week with the freshly generated
    /// assignment, all at the default (1x) portion multiplier, and returns the saved entries.
    /// </summary>
    private async Task<IReadOnlyList<WeekPlanEntryDto>> PersistWeekAssignmentAsync(
        DateOnly weekStartDate,
        SlotKey[] slotOrder,
        MealEntry[,] assignment,
        CancellationToken cancellationToken)
    {
        const decimal DefaultPortionMultiplier = 1m;

        DateOnly weekEndDate = weekStartDate.AddDays(DaysInWeek - 1);

        List<WeekPlanEntry> existingEntriesForWeek = await _db.WeekPlanEntries
            .Where(x => x.Date >= weekStartDate && x.Date <= weekEndDate)
            .ToListAsync(cancellationToken);
        _db.WeekPlanEntries.RemoveRange(existingEntriesForWeek);

        List<WeekPlanEntryDto> savedEntries = new(DaysInWeek * slotOrder.Length);

        for (int dayIndex = 0; dayIndex < DaysInWeek; dayIndex++)
        {
            DateOnly date = weekStartDate.AddDays(dayIndex);

            for (int slotIndex = 0; slotIndex < slotOrder.Length; slotIndex++)
            {
                SlotKey slotKey = slotOrder[slotIndex];
                MealEntry meal = assignment[dayIndex, slotIndex];

                _db.WeekPlanEntries.Add(new WeekPlanEntry(Guid.NewGuid(), date, slotKey, meal.Id, DefaultPortionMultiplier, null));
                savedEntries.Add(new WeekPlanEntryDto(date, slotKey, meal.Id, DefaultPortionMultiplier, null));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return savedEntries;
    }
}
