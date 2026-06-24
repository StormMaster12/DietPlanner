using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DietPlanner.Endpoints.DayPlan;

public static class DayPlanEndpoints
{
    public static IEndpointRouteBuilder MapDayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/day")
            .WithTags("DayPlan");

        group.MapGet("/{date}", GetDayAsync).WithOpenApi();

        return endpoints;
    }

    public static async Task<Ok<DayPlanResponseDto>> GetDayAsync([FromQuery] DateOnly date, [FromServices] IDayPlanService dayPlanService, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await dayPlanService.GetDayPlanAsync(date, cancellationToken));
    }
}