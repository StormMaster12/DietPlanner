using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Endpoints.Testing;

/// <summary>
/// Only mapped when the E2E_TESTING environment variable is set (the e2e suite's app container
/// sets it; real deployments never do), so this never exists as attack surface in production.
/// </summary>
public static class TestResetEndpoints
{
    public static void MapTestResetEndpoints(this WebApplication app)
    {
        app.MapPost("/__test__/reset", async Task<Ok> (AppDbContext db, CancellationToken cancellationToken) =>
        {
            // Slots are fixed reference data seeded once at startup and are never touched here -
            // everything else is per-test state the e2e suite needs wiped between tests. Deleted
            // in FK dependency order since Restrict prevents the DB from cascading for us.
            await db.WeekPlanEntries.ExecuteDeleteAsync(cancellationToken);
            await db.MealSuggestedAdditions.ExecuteDeleteAsync(cancellationToken);
            await db.MealAdditionSuggestionSets.ExecuteDeleteAsync(cancellationToken);
            await db.ShoppingListGroupingCache.ExecuteDeleteAsync(cancellationToken);
            await db.MealIngredients.ExecuteDeleteAsync(cancellationToken);
            await db.Meals.ExecuteDeleteAsync(cancellationToken);
            await db.Settings.ExecuteDeleteAsync(cancellationToken);
            return TypedResults.Ok();
        });
    }
}
