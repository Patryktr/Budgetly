using Budgetly.Api.Application;
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
        if (name is not { Length: >= 1 and <= 100 } || !BudgetPolicy.ValidMoney(request.Target) ||
            (request.Saved != 0 && !BudgetPolicy.ValidMoney(request.Saved)))
            return HandlerError.Invalid(new Dictionary<string, string[]>
            {
                ["goal"] = ["Podaj nazwę do 100 znaków, dodatni cel i nieujemną kwotę oszczędności."]
            });

        var query = db.Goals.Where(x => x.Id == id && x.UserId == userId);
        if (id is null)
        {
            var goal = new SavingsGoal { UserId = userId, Name = name, Target = request.Target, Saved = request.Saved };
            db.Goals.Add(goal);
            await db.SaveChangesAsync(ct);
            return HandlerResult<GoalResponse>.Success(new GoalResponse(goal.Id, name, goal.Target, goal.Saved, null));
        }
        var count = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Name, name).SetProperty(x => x.Target, request.Target).SetProperty(x => x.Saved, request.Saved)
            .SetProperty(x => x.CompletedAtUtc, x => request.Saved >= request.Target ? x.CompletedAtUtc : null), ct);
        if (count == 0) return HandlerError.NotFound();
        var response = await query.Select(x => new GoalResponse(x.Id, x.Name, x.Target, x.Saved, x.CompletedAtUtc))
            .SingleOrDefaultAsync(ct);
        return response is null ? HandlerError.NotFound() : HandlerResult<GoalResponse>.Success(response);
    }

    public async Task<HandlerResult<bool>> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var count = await db.Goals.Where(x => x.Id == id && x.UserId == userId).ExecuteDeleteAsync(ct);
        return count == 0 ? HandlerError.NotFound() : HandlerResult<bool>.Success(true);
    }
}
