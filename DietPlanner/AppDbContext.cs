using DietPlanner.Endpoints.DayPlan;
using DietPlanner.Endpoints.Meal;
using DietPlanner.Endpoints.Settings;
using DietPlanner.Endpoints.Slots;
using DietPlanner.Endpoints.WeekPlan;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Endpoints.Meal.MealEntry> Meals => Set<Endpoints.Meal.MealEntry>();
    public DbSet<Endpoints.Meal.MealIngredient> MealIngredients => Set<Endpoints.Meal.MealIngredient>();
    public DbSet<WeekPlanEntry> WeekPlanEntries => Set<WeekPlanEntry>();
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<MealAdditionSuggestionCacheEntry> MealAdditionSuggestionCache => Set<MealAdditionSuggestionCacheEntry>();
    public DbSet<ShoppingListGroupingCacheEntry> ShoppingListGroupingCache => Set<ShoppingListGroupingCacheEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ConfigureSlotEntity();
        b.ConfigureMealEntryEntity();
        b.ConfigureMealIngredientEntity();
        b.AddWeekPlanEntry();
        b.AddSettings();
        b.ConfigureMealAdditionSuggestionCacheEntryEntity();
        b.ConfigureShoppingListGroupingCacheEntryEntity();
    }
}
