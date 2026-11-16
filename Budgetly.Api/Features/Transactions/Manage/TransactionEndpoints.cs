using System.Data;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Transactions.Manage;

public static class TransactionEndpoints
{
    public sealed record TransactionPage(IReadOnlyList<CreateTransactionResponse> Items, int Total, int Page, int PageSize);

    public static void MapTransactionQueries(this WebApplication app)
    {
        var group = app.MapGroup("/api/transactions").RequireAuthorization().WithTags("Transactions");
        group.MapGet("", ListAsync).Produces<TransactionPage>();
        group.MapGet("/{id:guid}", GetAsync).Produces<CreateTransactionResponse>().Produces(404);
        group.MapPut("/{id:guid}", UpdateAsync).Produces<CreateTransactionResponse>()
            .ProducesValidationProblem().ProducesProblem(409).Produces(404);
        group.MapDelete("/{id:guid}", DeleteAsync).Produces(204).Produces(404);
    }

    private static async Task<IResult> ListAsync(DateOnly? from, DateOnly? to, TransactionType? type, Guid? categoryId,
        int? page, int? pageSize, HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        var currentPage = page ?? 1;
        var size = pageSize ?? 20;
        if (currentPage is < 1 or > 100000 || size is < 1 or > 100)
            return Invalid("page", "Numer strony musi być dodatni, a rozmiar strony musi wynosić od 1 do 100.");
        if (from > to)
            return Invalid("from", "Początek zakresu nie może wypadać po końcu.");
        if (type is not null && !Enum.IsDefined(type.Value))
            return Invalid("type", "Wybierz przychód albo wydatek.");

        var userId = JwtTokens.UserId(context.User);
        var query = db.Transactions.AsNoTracking().Where(x => x.UserId == userId);
        if (from is not null) query = query.Where(x => x.Date >= from);
        if (to is not null) query = query.Where(x => x.Date <= to);
        if (categoryId is not null) query = query.Where(x => x.CategoryId == categoryId);
        if (type == TransactionType.Income) query = query.Where(x => x.Amount > 0);
        if (type == TransactionType.Expense) query = query.Where(x => x.Amount < 0);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new TransactionPage(items.Select(x => Response(x)).ToList(), total, currentPage, size));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        var userId = JwtTokens.UserId(context.User);
        var item = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
        return item is null ? Results.NotFound() : Results.Ok(Response(item));
    }

    private static async Task<IResult> UpdateAsync(Guid id, CreateTransactionRequest request, HttpContext context,
        BudgetlyDbContext db, CancellationToken ct)
    {
        if (!BudgetPolicy.ValidMoney(request.Amount))
            return Invalid("amount", "Podaj dodatnią kwotę z maksymalnie dwoma miejscami po przecinku.");
        if (!Enum.IsDefined(request.Type) || request.Date == default || request.Description?.Length > 250)
            return Invalid("transaction", "Sprawdź typ, datę i opis transakcji.");

        var userId = JwtTokens.UserId(context.User);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await db.Transactions.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
        if (item is null)
            return Results.NotFound();
        var category = await db.Categories.SingleOrDefaultAsync(
            x => x.Id == request.CategoryId && (x.UserId == null || x.UserId == userId), ct);
        if (category is null || category.Type != request.Type)
            return Invalid("categoryId", "Wybierz dostępną kategorię zgodną z typem transakcji.");

        BudgetLimitWarning? warning = null;
        if (request.Type == TransactionType.Expense)
        {
            var evaluation = await BudgetPolicy.EvaluateAsync(db, userId, request.Date, request.Amount, id, ct);
            var reducesSameMonth = item.Amount < 0 && BudgetPolicy.Month(item.Date).Start == BudgetPolicy.Month(request.Date).Start
                && request.Amount <= -item.Amount;
            if (evaluation.Blocked && !reducesSameMonth)
                return Results.Problem(title: "Wydatek przekracza miesięczny limit o wysokim priorytecie.", statusCode: 409,
                    extensions: new Dictionary<string, object?> { ["budget"] = evaluation.Warning });
            warning = evaluation.Warning;
        }

        item.Update(request.CategoryId, request.Amount, request.Type, request.Date, request.Description);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(Response(item, warning));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        var userId = JwtTokens.UserId(context.User);
        var count = await db.Transactions.Where(x => x.UserId == userId && x.Id == id).ExecuteDeleteAsync(ct);
        return count == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static CreateTransactionResponse Response(Transaction x, BudgetLimitWarning? warning = null) =>
        new(x.Id, x.Amount, x.Amount > 0 ? TransactionType.Income : TransactionType.Expense,
            x.Date, x.CategoryId, x.Description, warning);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
