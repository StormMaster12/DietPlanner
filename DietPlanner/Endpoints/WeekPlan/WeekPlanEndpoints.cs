using DietPlanner.Endpoints.Slots;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DietPlanner.Endpoints.WeekPlan;

public static class WeekPlanEndpoints
{
    public static IEndpointRouteBuilder MapWeekEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/week")
            .WithTags("WeekPlan");

        group.MapGet("/", GetWeekAsync).WithOpenApi();
        group.MapPut("/entry", UpsertEntryAsync).WithOpenApi();
        group.MapPut("/set", SetWeekAsync).WithOpenApi();
        group.MapPost("/generate", GenerateWeekAsync).WithOpenApi();
        group.MapDelete("/entry/{date}/{slotKey}", DeleteEntryAsync).WithOpenApi();

        return endpoints;
    }

    public static async Task<Results<Ok<IReadOnlyList<WeekPlanEntryDto>>, BadRequest<string>>> GenerateWeekAsync(
        [FromBody] GenerateWeekPlanRequest req,
        [FromServices] IWeekPlanGeneratorService generatorService,
        CancellationToken cancellationToken)
    {
        var (result, missingSlot, entries) = await generatorService.GenerateWeekAsync(req, cancellationToken);
        return result switch
        {
            GenerateWeekPlanResult.NoMealsAvailableForSlot => TypedResults.BadRequest($"No meals exist for slot '{missingSlot}'. Add at least one meal for that slot before generating a week."),
            GenerateWeekPlanResult.Success => TypedResults.Ok(entries),
            _ => throw new NotImplementedException()
        };
    }

    public static async Task<Ok<IReadOnlyList<WeekPlanEntryDto>>> GetWeekAsync([FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromServices] IWeekPlanService weekPlanService, CancellationToken cancellationToken)
        => TypedResults.Ok(await weekPlanService.GetWeekAsync(start, end, cancellationToken));

    public static async Task<Results<ValidationProblem, BadRequest<string>, Ok<WeekPlanEntryDto>>> UpsertEntryAsync([FromBody] UpsertWeekEntryRequest req, [FromServices] IValidator<UpsertWeekEntryRequest> validator, [FromServices] IWeekPlanService weekPlanService, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(req, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var (result, entry) = await weekPlanService.UpsertEntryAsync(req, cancellationToken);
        return result switch
        {
            UpsertWeekEntryResult.InvalidSlot => TypedResults.BadRequest($"Invalid slot '{req.SlotKey}'"),
            UpsertWeekEntryResult.UnknownMeal => TypedResults.BadRequest($"Unknown meal '{req.MealId}'"),
            UpsertWeekEntryResult.Success when entry is not null => TypedResults.Ok(entry),
            _ => throw new NotImplementedException()
        };
    }

    public static async Task<Results<ValidationProblem, BadRequest<string>, NoContent>> SetWeekAsync([FromBody] IReadOnlyList<UpsertWeekEntryRequest> entries, [FromServices] IValidator<UpsertWeekEntryRequest> validator, [FromServices] IWeekPlanService weekPlanService, CancellationToken cancellationToken)
    {
        foreach (var e in entries)
        {
            var vr = await validator.ValidateAsync(e, cancellationToken);
            if (!vr.IsValid) return TypedResults.ValidationProblem(vr.ToDictionary());
        }

        try
        {
            var (result, count) = await weekPlanService.SetWeekAsync(entries, cancellationToken);
            return result switch
            {
                UpsertWeekEntryResult.InvalidSlot => TypedResults.BadRequest("Invalid slot in batch"),
                UpsertWeekEntryResult.UnknownMeal => TypedResults.BadRequest("Unknown meal in batch"),
                UpsertWeekEntryResult.Success => TypedResults.NoContent(),
                _ => throw new NotImplementedException()
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return TypedResults.BadRequest("entries must be between 1 and 28 items");
        }
    }

    public static async Task<Results<NoContent, NotFound>> DeleteEntryAsync([FromRoute] DateOnly date, [FromRoute] SlotKey slotKey, [FromServices] IWeekPlanService weekPlanService, CancellationToken cancellationToken)
    {
        var result = await weekPlanService.DeleteEntryAsync(date, slotKey, cancellationToken);
        return result switch
        {
            DeleteResult.NotFound => TypedResults.NotFound(),
            DeleteResult.Success => TypedResults.NoContent(),
            _ => throw new NotImplementedException()
        };
    }
}