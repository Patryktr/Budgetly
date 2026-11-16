namespace Budgetly.Api.Features.Budgets.SetMonthlyBudget;

public sealed record SetMonthlyBudgetRequest(decimal Limit, bool HighPriority = false);
