using Budgetly.Api.Application;
using System.Data;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Goals;

public sealed class GoalHandler(BudgetlyDbContext db)
{
    public Task<List<GoalResponse>> ListAsync(Guid userId, CancellationToken ct)
    {
        return db.Goals.Where(x => x.UserId == userId).OrderBy(x => x.Name)
            .Select(x => new GoalResponse(x.Id, x.Name, x.Target, x.Saved, x.CompletedAtUtc)).ToListAsync(ct);
    }

    public Task<HandlerResult<GoalResponse>> CreateAsync(Guid userId, GoalRequest request, CancellationToken ct) =>
        SaveAsync(userId, null, request, ct);

    public Task<HandlerResult<GoalResponse>> EditAsync(Guid userId, Guid id, GoalRequest request, CancellationToken ct) =>
        SaveAsync(userId, id, request, ct);

    private async Task<HandlerResult<GoalResponse>> SaveAsync(Guid userId, Guid? id, GoalRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 100 } || !BudgetPolicy.ValidMoney(request.Target))
            return HandlerError.Invalid(new Dictionary<string, string[]>
            {
                ["goal"] = ["Podaj nazwę do 100 znaków i dodatnią kwotę docelową."]
            });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (id is null)
        {
            if (request.Saved is not null and not 0)
                return HandlerError.Invalid("saved", "Nowy cel zaczyna od zera. Oszczędności dodawaj przez wpłaty.");
            var goal = new SavingsGoal { UserId = userId, Name = name, Target = request.Target };
            db.Goals.Add(goal);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return HandlerResult<GoalResponse>.Success(new GoalResponse(goal.Id, name, goal.Target, goal.Saved, null));
        }
        var existing = await db.Goals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (existing is null) return HandlerError.NotFound();
        if (request.Saved is { } saved && saved != existing.Saved)
            return HandlerError.Invalid("saved", "Kwotę oszczędności zmieniaj przez wpłaty lub korektę powiązanej transakcji.");
        existing.Name = name;
        existing.Target = request.Target;
        existing.AdjustSaved(0);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return HandlerResult<GoalResponse>.Success(new GoalResponse(existing.Id, existing.Name, existing.Target, existing.Saved, existing.CompletedAtUtc));
    }

    public async Task<HandlerResult<bool>> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var goal = await db.Goals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (goal is null) return HandlerError.NotFound();
        await db.Transactions.Where(x => x.UserId == userId && x.GoalId == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.GoalId, (Guid?)null), ct);
        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return HandlerResult<bool>.Success(true);
    }
}
