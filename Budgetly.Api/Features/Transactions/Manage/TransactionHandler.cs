using Budgetly.Api.Application;
using System.Data;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Goals;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Transactions.Manage;

public sealed class TransactionHandler(BudgetlyDbContext db)
{
    public async Task<HandlerResult<TransactionPage>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, TransactionType? type, Guid? categoryId,
        int? page, int? pageSize, CancellationToken ct)
    {
        var currentPage = page ?? 1;
        var size = pageSize ?? 20;
        if (currentPage is < 1 or > 100000 || size is < 1 or > 100)
            return Invalid("page", "Numer strony musi być dodatni, a rozmiar strony musi wynosić od 1 do 100.");
        if (from > to)
            return Invalid("from", "Początek zakresu nie może wypadać po końcu.");
        if (type is not null && !Enum.IsDefined(type.Value))
            return Invalid("type", "Wybierz przychód albo wydatek.");

        var query = db.Transactions.AsNoTracking().Where(x => x.UserId == userId);
        if (from is not null) query = query.Where(x => x.Date >= from);
        if (to is not null) query = query.Where(x => x.Date <= to);
        if (categoryId is not null) query = query.Where(x => x.CategoryId == categoryId);
        if (type == TransactionType.Income) query = query.Where(x => x.Amount > 0);
        if (type == TransactionType.Expense) query = query.Where(x => x.Amount < 0);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        return HandlerResult<TransactionPage>.Success(new TransactionPage(items.Select(x => Response(x)).ToList(), total, currentPage, size));
    }

    public async Task<HandlerResult<CreateTransactionResponse>> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var item = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
        return item is null ? HandlerError.NotFound() : HandlerResult<CreateTransactionResponse>.Success(Response(item));
    }

    public async Task<HandlerResult<CreateTransactionResponse>> UpdateAsync(Guid userId, Guid id, CreateTransactionRequest request, CancellationToken ct)
    {
        if (!BudgetPolicy.ValidMoney(request.Amount))
            return Invalid("amount", "Podaj dodatnią kwotę z maksymalnie dwoma miejscami po przecinku.");
        if (!Enum.IsDefined(request.Type) || request.Date == default || request.Description?.Length > 250)
            return Invalid("transaction", "Sprawdź typ, datę i opis transakcji.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await db.Transactions.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
        if (item is null)
            return HandlerError.NotFound();
        if (item.IsSavings && (request.Type != TransactionType.Expense || request.CategoryId != item.CategoryId))
            return Invalid("transaction", "Wpłata na oszczędności musi zachować swój typ i kategorię.");
        var category = await db.Categories.SingleOrDefaultAsync(
            x => x.Id == request.CategoryId && (x.UserId == null || x.UserId == userId), ct);
        if (category is null || category.Type != request.Type)
            return Invalid("categoryId", "Wybierz dostępną kategorię zgodną z typem transakcji.");

        BudgetLimitWarning? warning = null;
        if (request.Type == TransactionType.Expense && !item.IsSavings)
        {
            var evaluation = await BudgetPolicy.EvaluateAsync(db, userId, request.Date, request.Amount, id, ct);
            var reducesSameMonth = item.Amount < 0 && BudgetPolicy.Month(item.Date).Start == BudgetPolicy.Month(request.Date).Start
                && request.Amount <= -item.Amount;
            if (evaluation.Blocked && !reducesSameMonth)
                return HandlerError.Conflict("Wydatek przekracza miesięczny limit o wysokim priorytecie.", details: new Dictionary<string, object?> { ["budget"] = evaluation.Warning });
            warning = evaluation.Warning;
        }

        if (!item.IsSavings && request.CategoryId == SavingsGoal.CategoryId)
            return Invalid("categoryId", "Wpłatę na oszczędności dodaj przy wybranym celu.");

        if (item.GoalId is { } goalId)
        {
            var goal = await db.Goals.SingleAsync(x => x.Id == goalId && x.UserId == userId, ct);
            var difference = request.Amount + item.Amount;
            var nextSaved = goal.Saved + difference;
            if (nextSaved != 0 && !BudgetPolicy.ValidMoney(nextSaved))
                return Invalid("amount", "Łączna kwota oszczędności musi wynosić od 0 do 999 999 999,99 zł.");
            goal.AdjustSaved(difference);
        }
        item.Update(request.CategoryId, request.Amount, request.Type, request.Date, request.Description);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return HandlerResult<CreateTransactionResponse>.Success(Response(item, warning));
    }

    public async Task<HandlerResult<bool>> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await db.Transactions.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
        if (item is null) return HandlerError.NotFound();
        if (item.GoalId is { } goalId)
        {
            var goal = await db.Goals.SingleAsync(x => x.Id == goalId && x.UserId == userId, ct);
            goal.AdjustSaved(item.Amount);
        }
        db.Transactions.Remove(item);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return HandlerResult<bool>.Success(true);
    }

    private static CreateTransactionResponse Response(Transaction x, BudgetLimitWarning? warning = null) =>
        new(x.Id, x.Amount, x.Amount > 0 ? TransactionType.Income : TransactionType.Expense,
            x.Date, x.CategoryId, x.Description, warning, x.GoalId, x.IsSavings);

    private static HandlerError Invalid(string field, string message) =>
        HandlerError.Invalid(new Dictionary<string, string[]> { [field] = [message] });
}
