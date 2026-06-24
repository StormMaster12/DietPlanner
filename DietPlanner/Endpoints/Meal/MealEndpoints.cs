using DietPlanner.Endpoints.Slots;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DietPlanner.Endpoints.Meal;

public static class MealsEndpoints
{
    public static IEndpointRouteBuilder MapMealsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/meals")
            .WithTags("Meals");

        group.MapGet("/", GetMealsAsync).WithOpenApi();
        group.MapGet("/{mealId}", GetMealAsync).WithOpenApi();
        group.MapPut("/", UpsertMealAsync).WithOpenApi();
        group.MapDelete("/{mealId}", DeleteMealAsync).WithOpenApi();

        return endpoints;
    }

    public static async Task<Ok<IReadOnlyList<MealDto>>> GetMealsAsync([FromServices] IMealsService mealsService, CancellationToken cancellationToken)
        => TypedResults.Ok(await mealsService.GetMealsAsync(cancellationToken));

    public static async Task<Results<Ok<MealDto>, NotFound>> GetMealAsync([FromRoute] Guid mealId, [FromServices] IMealsService mealsService, CancellationToken cancellationToken)
    {
        var meal = await mealsService.GetMealAsync(mealId, cancellationToken);
        return meal is null ? TypedResults.NotFound() : TypedResults.Ok(meal);
    }

    public static async Task<Results<ValidationProblem, BadRequest<string>, Ok<MealDto>>> UpsertMealAsync([FromBody] UpsertMealRequest req, [FromServices] IValidator<UpsertMealRequest> validator, [FromServices] IMealsService mealsService, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(req, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var (result, meal) = await mealsService.UpsertMealAsync(req, cancellationToken);
        return result switch
        {
            UpsertMealResult.InvalidSlot => TypedResults.BadRequest($"Invalid slot '{req.SlotKey}'"),
            UpsertMealResult.Success when meal is not null => TypedResults.Ok(meal),
            _ => throw new NotImplementedException()
        };
    }

    public static async Task<Results<NoContent, NotFound>> DeleteMealAsync([FromRoute] Guid mealId, [FromServices] IMealsService mealsService, CancellationToken cancellationToken)
    {
        DeleteResult result = await mealsService.DeleteMealAsync(mealId, cancellationToken);
        return result switch
        {
            DeleteResult.NotFound => TypedResults.NotFound(),
            DeleteResult.Success => TypedResults.NoContent(),
            _ => throw new NotImplementedException()
        };
    }
}