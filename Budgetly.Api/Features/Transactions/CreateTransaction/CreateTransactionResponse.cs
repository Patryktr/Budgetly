using Budgetly.Api.Features.Transactions;

namespace Budgetly.Api.Features.Transactions.CreateTransaction;

public sealed record CreateTransactionResponse(
    Guid Id,
    decimal Amount,
    TransactionType Type,
    DateOnly Date,
    Guid CategoryId,
    string? Description,
    BudgetLimitWarning? BudgetWarning,
    Guid? GoalId = null,
    bool IsSavings = false);

public sealed record BudgetLimitWarning(decimal Limit, decimal ProjectedSpending);
