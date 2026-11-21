using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Transactions.Manage;

public static class TransactionEndpoints
{
    public static void MapTransactionQueries(this WebApplication app)
    {
        var group = app.MapGroup("/api/transactions").RequireAuthorization().WithTags("Transactions");
        group.MapGet("", ListAsync).Produces<TransactionPage>();
        group.MapGet("/{id:guid}", async (Guid id, HttpContext context, TransactionHandler handler, CancellationToken ct) =>
            (await handler.GetAsync(JwtTokens.UserId(context.User), id, ct)).ToHttpResult())
            .Produces<CreateTransactionResponse>().Produces(404);
        group.MapPut("/{id:guid}", async (Guid id, CreateTransactionRequest request, HttpContext context, TransactionHandler handler, CancellationToken ct) =>
            (await handler.UpdateAsync(JwtTokens.UserId(context.User), id, request, ct)).ToHttpResult())
            .Produces<CreateTransactionResponse>().ProducesValidationProblem().ProducesProblem(409).Produces(404);
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext context, TransactionHandler handler, CancellationToken ct) =>
            (await handler.DeleteAsync(JwtTokens.UserId(context.User), id, ct)).ToHttpResult(StatusCodes.Status204NoContent))
            .Produces(204).Produces(404);
    }

    private static async Task<IResult> ListAsync(DateOnly? from, DateOnly? to, TransactionType? type, Guid? categoryId,
        int? page, int? pageSize, HttpContext context, TransactionHandler handler, CancellationToken ct) =>
        (await handler.ListAsync(JwtTokens.UserId(context.User), from, to, type, categoryId, page, pageSize, ct)).ToHttpResult();
}
