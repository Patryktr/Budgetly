using System.Data;
using Budgetly.Api.Application;
using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Budgets.SetMonthlyBudget;

public sealed class SetMonthlyBudgetHandler(BudgetlyDbContext db)
{
    public async Task<HandlerResult<SetMonthlyBudgetResponse>> HandleAsync(
        Guid userId, string month, SetMonthlyBudgetRequest request, CancellationToken ct)
    {
        if (!BudgetPolicy.TryParseMonth(month, out var monthStart))
            return HandlerError.Invalid("month", "Użyj formatu RRRR-MM.");

        if (!BudgetPolicy.ValidMoney(request.Limit))
            return HandlerError.Invalid("limit",
                "Limit musi wynosić od 0,01 do 999 999 999,99 i mieć maksymalnie dwa miejsca po przecinku.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var budget = await db.MonthlyBudgets.SingleOrDefaultAsync(
            x => x.UserId == userId && x.MonthStart == monthStart, ct);

        if (budget is null)
        {
            budget = new MonthlyBudget(userId, monthStart);
            db.MonthlyBudgets.Add(budget);
        }

        budget.SetLimit(request.Limit, request.HighPriority);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return HandlerResult<SetMonthlyBudgetResponse>.Success(
            new SetMonthlyBudgetResponse(month, budget.Limit, budget.HighPriority));
    }
}
