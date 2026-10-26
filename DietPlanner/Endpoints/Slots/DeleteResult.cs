using Microsoft.AspNetCore.Http.HttpResults;

namespace DietPlanner.Endpoints.Slots;

public enum DeleteResult
{
    NotFound,
    Success,
}

public static class DeleteResultExtensions
{
    public static Results<NoContent, NotFound> ToHttpResult(this DeleteResult result) => result switch
    {
        DeleteResult.NotFound => TypedResults.NotFound(),
        DeleteResult.Success => TypedResults.NoContent(),
        _ => throw new NotImplementedException()
    };
}
