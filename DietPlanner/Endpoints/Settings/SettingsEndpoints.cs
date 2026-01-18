using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DietPlanner.Endpoints.Settings;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/settings")
            .WithTags("Settings");

        _ = group.MapGet("/", GetSettings).WithOpenApi();
        _ = group.MapPut("/", PutSettings).WithOpenApi();

        return endpoints;
    }

    public static Task<SettingsDto> GetSettings([FromServices] ISettingsService svc, CancellationToken ct)
    {
        return svc.GetAsync(ct);
    }

    public static async Task<Results<ValidationProblem, Ok<SettingsDto>>> PutSettings([FromBody] UpdateSettingsRequest req, [FromServices] ISettingsService svc, [FromServices] IValidator<UpdateSettingsRequest> validator, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult valid = await validator.ValidateAsync(req, ct);
        if (!valid.IsValid)
        {
            return TypedResults.ValidationProblem(valid.ToDictionary());
        }

        SettingsDto updated = await svc.UpdateAsync(req, ct);
        return TypedResults.Ok(updated);
    }
}
