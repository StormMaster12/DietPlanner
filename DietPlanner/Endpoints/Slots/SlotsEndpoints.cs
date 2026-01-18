using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace DietPlanner.Endpoints.Slots;

public static class SlotsEndpoints
{
    public static IEndpointRouteBuilder MapSlotsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/slots")
            .WithTags("Slots");

        group.MapGet("/", GetSlotsAsync).WithOpenApi();
        group.MapPost("/", CreateSlotAsync).WithOpenApi();
        group.MapDelete("/{key}", DeleteSlotsAsync).WithOpenApi();

        return endpoints;
    }

    public static async Task<Results<NoContent, NotFound>> DeleteSlotsAsync(SlotKey key, ISlotsService slotsService, CancellationToken cancellationToken)
    {
        var result = await slotsService.DeleteSlotAsync(key, cancellationToken);
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
        var validationResult = await validator.ValidateAsync(req, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var result = await slotsService.CreateSlotAsync(req, cancellationToken);
        if (result is null)
        {
            return TypedResults.Conflict(new ConflictResult($"A slot with key '{req.Key}' already exists."));
        }


        return TypedResults.Ok(result);
    }
}

public interface ISlotsService
{
    Task<IEnumerable<SlotDto>> GetSlotsAsync(CancellationToken cancellationToken);
    Task<SlotDto?> CreateSlotAsync(CreateSlotRequest request, CancellationToken cancellationToken);
    Task<DeleteResult> DeleteSlotAsync(SlotKey key, CancellationToken cancellationToken);
}

public class SlotsService : ISlotsService
{
    private readonly AppDbContext _appDbContext;

    public SlotsService(AppDbContext appDbContext)
    {
        _appDbContext = appDbContext;
    }

    public async Task<SlotDto?> CreateSlotAsync(CreateSlotRequest request, CancellationToken cancellationToken)
    {
        var exists = await _appDbContext.Slots.AnyAsync(s => s.Key == request.Key);
        if (exists)
        {
            return null;
        }

        var slot = new Slot(request.Key, request.DisplayName.Trim(), request.SortOrder);

        _appDbContext.Slots.Add(slot);
        await _appDbContext.SaveChangesAsync();

        return new SlotDto(slot.DisplayName, slot.SortOrder, slot.Key);
    }

    public async Task<DeleteResult> DeleteSlotAsync(SlotKey key, CancellationToken cancellationToken)
    {
        var slot = await _appDbContext.Slots.SingleOrDefaultAsync(s => s.Key == key);
        if (slot is null) return DeleteResult.NotFound;

        _appDbContext.Slots.Remove(slot);
        await _appDbContext.SaveChangesAsync();
        return DeleteResult.Success;
    }

    public async Task<IEnumerable<SlotDto>> GetSlotsAsync(CancellationToken cancellationToken)
    {
        var items = await _appDbContext.Slots
                .OrderBy(s => s.SortOrder)
                .Select(s => new SlotDto(s.DisplayName, s.SortOrder, s.Key))
                .ToListAsync();

        return items;
    }
}

public enum SlotKey
{
    Breakfast,
    Lunch,
    Dinner,
    Snack
}

public record CreateSlotRequest(
    SlotKey Key,
    string DisplayName,
    int SortOrder)
{
    public class CreateSlotRequestValidator : AbstractValidator<CreateSlotRequest>
    {
        public CreateSlotRequestValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().WithMessage("Key is required");
            RuleFor(x => x.DisplayName)
                .NotEmpty().WithMessage("DisplayName is required")
                .MaximumLength(100).WithMessage("DisplayName must be at most 100 characters long");
            RuleFor(x => x.SortOrder)
                .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be non-negative");
        }
    };
}
public record Slot(
    SlotKey Key,
    string DisplayName,
    int SortOrder);

public record SlotDto(
    string DisplayName,
    int SortOrder,
    SlotKey Key);

public enum DeleteResult
{
    NotFound,
    Success,
}

public record ConflictResult(string Message);