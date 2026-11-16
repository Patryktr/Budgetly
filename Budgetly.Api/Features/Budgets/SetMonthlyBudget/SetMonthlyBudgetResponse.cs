namespace Budgetly.Api.Features.Budgets.SetMonthlyBudget;

public sealed record SetMonthlyBudgetResponse(string Month, decimal Limit, bool HighPriority);
