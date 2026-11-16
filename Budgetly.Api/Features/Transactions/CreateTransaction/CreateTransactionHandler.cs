using System.Data;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Transactions.CreateTransaction;

public sealed class CreateTransactionHandler(BudgetlyDbContext dbContext)
{
    public async Task<CreateTransactionResult> HandleAsync(
        Guid userId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (!BudgetPolicy.ValidMoney(request.Amount))
            return CreateTransactionResult.Invalid("Amount", "Podaj kwotę od 0,01 do 999 999 999,99 z maksymalnie dwoma miejscami po przecinku.");

        if (!Enum.IsDefined(request.Type))
            return CreateTransactionResult.Invalid("Type", "Typ musi mieć wartość Income albo Expense.");

        if (request.Date == default)
            return CreateTransactionResult.Invalid("Date", "Podaj datę transakcji.");

        if (request.Description?.Length > 250)
            return CreateTransactionResult.Invalid("Description", "Opis może mieć maksymalnie 250 znaków.");

        var category = await dbContext.Categories
            .SingleOrDefaultAsync(x => x.Id == request.CategoryId && (x.UserId == null || x.UserId == userId), cancellationToken);

        if (category is null)
            return CreateTransactionResult.CategoryNotFound();

        if (category.Type != request.Type)
            return CreateTransactionResult.Invalid("CategoryId", "Kategoria musi odpowiadać typowi transakcji.");

        var transaction = Transaction.Create(
            userId,
            request.CategoryId,
            request.Amount,
            request.Type,
            request.Date,
            request.Description);

        // An expense must check the monthly total and save within the same database transaction.
        await using var dbTransaction = request.Type == TransactionType.Expense
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        BudgetLimitWarning? warning = null;

        if (request.Type == TransactionType.Expense)
        {
            var evaluation = await BudgetPolicy.EvaluateAsync(dbContext, userId, transaction.Date,
                request.Amount, null, cancellationToken);
            if (evaluation.Blocked)
                return CreateTransactionResult.BudgetExceeded(evaluation.Warning!);
            warning = evaluation.Warning;
        }

        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (dbTransaction is not null)
            await dbTransaction.CommitAsync(cancellationToken);

        return CreateTransactionResult.Created(new CreateTransactionResponse(
            transaction.Id,
            transaction.Amount,
            request.Type,
            transaction.Date,
            transaction.CategoryId,
            transaction.Description,
            warning));
    }
}

public sealed record CreateTransactionResult(
    CreateTransactionResponse? Response,
    string? Error,
    string? ErrorField,
    bool NotFound,
    BudgetLimitWarning? ExceededBudget = null)
{
    public static CreateTransactionResult Created(CreateTransactionResponse response) => new(response, null, null, false);
    public static CreateTransactionResult Invalid(string field, string error) => new(null, error, field, false);
    public static CreateTransactionResult CategoryNotFound() => new(null, "Nie znaleziono kategorii.", null, true);
    public static CreateTransactionResult BudgetExceeded(BudgetLimitWarning budget) => new(null, null, null, false, budget);
}
