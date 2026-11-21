using Budgetly.Api.Application;

namespace Budgetly.Api.Infrastructure;

public static class HandlerHttpResults
{
    public static IResult ToHttpResult<T>(this HandlerResult<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.Error is { } error)
            return error.Kind switch
            {
                HandlerErrorKind.Validation => Results.ValidationProblem(error.ValidationErrors!),
                HandlerErrorKind.NotFound => Results.NotFound(),
                HandlerErrorKind.Unauthorized => Results.Unauthorized(),
                HandlerErrorKind.Conflict => Results.Problem(
                    title: error.Message, statusCode: StatusCodes.Status409Conflict, extensions: error.Details),
                _ => throw new InvalidOperationException("Unknown handler result.")
            };

        return successStatus == StatusCodes.Status204NoContent
            ? Results.NoContent()
            : Results.Json(result.Value, statusCode: successStatus);
    }
}
