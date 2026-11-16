using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Budgets;

public static class MonthlyBudgetEndpoints
{
    public sealed record CategoryTotal(Guid CategoryId, string Name, decimal Spending);
    public sealed record MonthlySummary(decimal Income, decimal Spending, decimal Balance, decimal? Limit,
        bool HighPriority, IReadOnlyList<CategoryTotal> Categories);

    public static void MapMonthlySummary(this WebApplication app)
    {
        var group = app.MapGroup("/api/budgets").RequireAuthorization().WithTags("Budgets");
        group.MapGet("/{month}", SummaryAsync);
        group.MapDelete("/{month}", DeleteAsync);
    }

    private static async Task<IResult> SummaryAsync(string month, HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        if (!BudgetPolicy.TryParseMonth(month, out var date)) return InvalidMonth();
        var userId = JwtTokens.UserId(context.User);
        var (start, end) = BudgetPolicy.Month(date);
        var transactions = db.Transactions.Where(x => x.UserId == userId && x.Date >= start && x.Date <= end);
        var income = await transactions.Where(x => x.Amount > 0).SumAsync(x => x.Amount, ct);
        var categorySums = await transactions.Where(x => x.Amount < 0)
            .GroupBy(x => new { x.CategoryId, x.Category!.Name })
            .Select(g => new { g.Key.CategoryId, g.Key.Name, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var expenses = categorySums.Select(x => new CategoryTotal(x.CategoryId, x.Name, -x.Amount))
            .OrderByDescending(x => x.Spending).ToList();
        var budget = await db.MonthlyBudgets.FindAsync([userId, start], ct);
        var spending = expenses.Sum(x => x.Spending);
        return Results.Ok(new MonthlySummary(income, spending, income - spending, budget?.Limit,
            budget?.HighPriority ?? false, expenses));
    }

    private static async Task<IResult> DeleteAsync(string month, HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        if (!BudgetPolicy.TryParseMonth(month, out var date)) return InvalidMonth();
        var userId = JwtTokens.UserId(context.User);
        var count = await db.MonthlyBudgets.Where(x => x.UserId == userId && x.MonthStart == date).ExecuteDeleteAsync(ct);
        return count == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static IResult InvalidMonth() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["month"] = ["Użyj formatu RRRR-MM."]
    });
}
