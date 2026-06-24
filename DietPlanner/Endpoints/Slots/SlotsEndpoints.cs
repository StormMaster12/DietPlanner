using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DietPlanner.Endpoints.Slots;

public static class SlotsEndpoints
{
    public static IEndpointRouteBuilder MapSlotsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/slots")
            .WithTags("Slots");

        group.MapGet("/", GetSlotsAsync).WithOpenApi();
        group.MapPost("/", CreateSlotAsync).WithOpenApi();
        group.MapDelete("/{key}", DeleteSlotsAsync).WithOpenApi();

        return endpoints;
    }

    public static async Task<Results<NoContent, NotFound>> DeleteSlotsAsync(SlotKey key, ISlotsService slotsService, CancellationToken cancellationToken)
    {
        DeleteResult result = await slotsService.DeleteSlotAsync(key, cancellationToken);
        return result switch
        {
            DeleteResult.NotFound => TypedResults.NotFound(),
            DeleteResult.Success => TypedResults.NoContent(),
            _ => throw new NotImplementedException()
        };
    }

    public static async Task<Ok<IEnumerable<SlotDto>>> GetSlotsAsync(ISlotsService slotsService, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await slotsService.GetSlotsAsync(cancellationToken));
    }

    public static async Task<Results<ValidationProblem, Conflict<ConflictResult>, Ok<SlotDto>>> CreateSlotAsync(CreateSlotRequest req, IValidator<CreateSlotRequest> validator, ISlotsService slotsService, CancellationToken cancellationToken)
    {
        FluentValidation.Results.ValidationResult validationResult = await validator.ValidateAsync(req, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        SlotDto? result = await slotsService.CreateSlotAsync(req, cancellationToken);
        return result is null
            ? (Results<ValidationProblem, Conflict<ConflictResult>, Ok<SlotDto>>)TypedResults.Conflict(new ConflictResult($"A slot with key '{req.Key}' already exists."))
            : (Results<ValidationProblem, Conflict<ConflictResult>, Ok<SlotDto>>)TypedResults.Ok(result);
    }
}
