using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DietPlanner.Endpoints.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/settings")
            .WithTags("Settings");

        group.MapGet("/", GetSettings).WithOpenApi();
        group.MapPut("/", PutSettings).WithOpenApi();

        return endpoints;
    }

    public static async Task<Ok<SettingsDto>> GetSettings([FromServices] ISettingsService svc, CancellationToken ct)
        => TypedResults.Ok(await svc.GetAsync(ct));

    public static async Task<Results<ValidationProblem, Ok<SettingsDto>>> PutSettings([FromBody] UpdateSettingsRequest req, [FromServices] ISettingsService svc, [FromServices] IValidator<UpdateSettingsRequest> validator, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validationResult = await validator.ValidateAsync(req, ct);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        SettingsDto updated = await svc.UpdateAsync(req, ct);
        return TypedResults.Ok(updated);
    }
}
