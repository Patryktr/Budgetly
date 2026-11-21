using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth;

public sealed record AdminSummary(int UserCount);

public sealed class AdminSummaryHandler(BudgetlyDbContext db)
{
    public async Task<AdminSummary> HandleAsync(CancellationToken ct) => new(await db.Users.CountAsync(ct));
}
