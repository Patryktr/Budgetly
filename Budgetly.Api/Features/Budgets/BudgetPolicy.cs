using Budgetly.Api.Data;
using System.Globalization;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Budgets;

public static class BudgetPolicy
{
    public static bool TryParseMonth(string month, out DateOnly date) =>
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static (DateOnly Start, DateOnly End) Month(DateOnly date) =>
        (new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)));

    public static bool ValidMoney(decimal amount) =>
        amount is >= 0.01m and <= 999999999.99m && decimal.Round(amount, 2) == amount;

    public static bool IsOverLimit(decimal limit, decimal spent, decimal expense) => spent + expense > limit;

    public static async Task<(BudgetLimitWarning? Warning, bool Blocked)> EvaluateAsync(
        BudgetlyDbContext db, Guid userId, DateOnly date, decimal expense, Guid? excludedId, CancellationToken ct)
    {
        var (start, end) = Month(date);
        var budget = await db.MonthlyBudgets.FindAsync([userId, start], ct);
        if (budget is null)
            return (null, false);

        var expenses = db.Transactions.Where(x => x.UserId == userId && x.Date >= start && x.Date <= end && x.Amount < 0 && !x.IsSavings);
        if (excludedId is { } id)
            expenses = expenses.Where(x => x.Id != id);
        var spent = -await expenses.SumAsync(x => x.Amount, ct);
        return IsOverLimit(budget.Limit, spent, expense)
            ? (new BudgetLimitWarning(budget.Limit, spent + expense), budget.HighPriority)
            : (null, false);
    }
}
