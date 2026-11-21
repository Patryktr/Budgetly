namespace Budgetly.Api.Features.Budgets;

public sealed record CategoryTotal(Guid CategoryId, string Name, decimal Spending);
public sealed record MonthSpending(string Month, decimal Spending);
public sealed record MonthlySummary(decimal Income, decimal Spending, decimal Balance, decimal? Limit,
    bool HighPriority, IReadOnlyList<CategoryTotal> Categories, IReadOnlyList<MonthSpending> SpendingTrend, decimal Savings = 0);
