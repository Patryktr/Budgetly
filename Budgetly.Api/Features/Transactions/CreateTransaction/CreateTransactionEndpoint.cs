using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Budgetly.Api.Features.Transactions.CreateTransaction;

public static class CreateTransactionEndpoint
{
    public static IEndpointRouteBuilder MapCreateTransactionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/transactions", HandleAsync)
            .WithName("CreateTransaction")
            .WithTags("Transactions")
            .RequireAuthorization()
            .Produces<CreateTransactionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateTransactionRequest request,
        HttpContext httpContext,
        CreateTransactionHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var result = await handler.HandleAsync(userId, request, cancellationToken);
        if (result.ExceededBudget is { } exceeded)
            return Results.Problem(
                title: "Wydatek przekracza miesięczny limit o wysokim priorytecie.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["budget"] = exceeded });

        if (result.NotFound)
            return Results.NotFound(new ProblemDetails { Title = result.Error, Status = StatusCodes.Status404NotFound });
        if (result.Error is not null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [result.ErrorField!] = [result.Error] });

        return Results.Created($"/api/transactions/{result.Response!.Id}", result.Response);
    }
}
