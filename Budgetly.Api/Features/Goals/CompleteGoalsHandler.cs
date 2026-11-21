using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Goals;

public sealed class CompleteGoalsHandler(BudgetlyDbContext db)
{
    public Task<int> HandleAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.Goals.Where(x => x.CompletedAtUtc == null && x.Saved >= x.Target)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CompletedAtUtc, now), ct);
    }
}
