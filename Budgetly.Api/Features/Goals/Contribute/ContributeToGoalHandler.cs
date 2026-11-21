using System.Data;
using Budgetly.Api.Application;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Transactions;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Goals.Contribute;

public sealed class ContributeToGoalHandler(BudgetlyDbContext db)
{
    public async Task<HandlerResult<ContributeToGoalResponse>> HandleAsync(
        Guid userId, Guid goalId, ContributeToGoalRequest request, CancellationToken ct)
    {
        if (!BudgetPolicy.ValidMoney(request.Amount) || request.Date == default)
            return HandlerError.Invalid("contribution", "Podaj dodatnią kwotę z maksymalnie dwoma miejscami po przecinku i datę wpłaty.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var goal = await db.Goals.SingleOrDefaultAsync(x => x.Id == goalId && x.UserId == userId, ct);
        if (goal is null) return HandlerError.NotFound();
        if (!BudgetPolicy.ValidMoney(goal.Saved + request.Amount))
            return HandlerError.Invalid("amount", "Łączna kwota oszczędności nie może przekroczyć 999 999 999,99 zł.");

        // A savings transfer reduces available money, independently of the monthly expense limit.
        var transfer = Transaction.Create(userId, SavingsGoal.CategoryId, request.Amount,
            TransactionType.Expense, request.Date, "Wpłata na cel: " + goal.Name, goalId: goal.Id);
        goal.AdjustSaved(request.Amount);
        db.Transactions.Add(transfer);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return HandlerResult<ContributeToGoalResponse>.Success(new(
            new GoalResponse(goal.Id, goal.Name, goal.Target, goal.Saved, goal.CompletedAtUtc),
            new CreateTransactionResponse(transfer.Id, transfer.Amount, TransactionType.Expense,
                transfer.Date, transfer.CategoryId, transfer.Description, null, goal.Id, true)));
    }
}
