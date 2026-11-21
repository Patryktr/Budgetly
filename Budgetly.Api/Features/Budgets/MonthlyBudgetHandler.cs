using Budgetly.Api.Application;
using System.Globalization;
using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Budgets;

public sealed class MonthlyBudgetHandler(BudgetlyDbContext db)
{
    private const int TrendMonths = 6;

    public async Task<HandlerResult<MonthlySummary>> SummaryAsync(Guid userId, string month, CancellationToken ct)
    {
        if (!BudgetPolicy.TryParseMonth(month, out var date)) return InvalidMonth();
        var (start, end) = BudgetPolicy.Month(date);
        var transactions = db.Transactions.Where(x => x.UserId == userId && x.Date >= start && x.Date <= end);
        var income = await transactions.Where(x => x.Amount > 0).SumAsync(x => x.Amount, ct);
        var savings = -await transactions.Where(x => x.IsSavings).SumAsync(x => x.Amount, ct);
        var categorySums = await transactions.Where(x => x.Amount < 0 && !x.IsSavings)
            .GroupBy(x => new { x.CategoryId, x.Category!.Name })
            .Select(g => new { g.Key.CategoryId, g.Key.Name, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var expenses = categorySums.Select(x => new CategoryTotal(x.CategoryId, x.Name, -x.Amount))
            .OrderByDescending(x => x.Spending).ToList();
        var budget = await db.MonthlyBudgets.FindAsync([userId, start], ct);
        var spending = expenses.Sum(x => x.Spending);
        var trend = await LoadSpendingTrendAsync(userId, start, end, ct);
        return HandlerResult<MonthlySummary>.Success(new MonthlySummary(income, spending, income - spending - savings, budget?.Limit,
            budget?.HighPriority ?? false, expenses, trend, savings));
    }

    private async Task<List<MonthSpending>> LoadSpendingTrendAsync(
        Guid userId, DateOnly selectedMonth, DateOnly end, CancellationToken ct)
    {
        var monthCount = Math.Min(TrendMonths, (selectedMonth.Year - 1) * 12 + selectedMonth.Month);
        var firstMonth = selectedMonth.AddMonths(1 - monthCount);
        var monthlyTotals = await db.Transactions
            .Where(x => x.UserId == userId && x.Amount < 0 && !x.IsSavings && x.Date >= firstMonth && x.Date <= end)
            .GroupBy(x => new { x.Date.Year, x.Date.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => (x.Year, x.Month), x => -x.Total, ct);

        return Enumerable.Range(0, monthCount).Select(firstMonth.AddMonths)
            .Select(date => new MonthSpending(date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                monthlyTotals.GetValueOrDefault((date.Year, date.Month))))
            .ToList();
    }

    public async Task<HandlerResult<bool>> DeleteAsync(Guid userId, string month, CancellationToken ct)
    {
        if (!BudgetPolicy.TryParseMonth(month, out var date)) return InvalidMonth();
        var count = await db.MonthlyBudgets.Where(x => x.UserId == userId && x.MonthStart == date).ExecuteDeleteAsync(ct);
        return count == 0 ? HandlerError.NotFound() : HandlerResult<bool>.Success(true);
    }

    private static HandlerError InvalidMonth() => HandlerError.Invalid(new Dictionary<string, string[]>
    {
        ["month"] = ["Użyj formatu RRRR-MM."]
    });
}
