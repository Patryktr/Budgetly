using Budgetly.Api.Features.Transactions.CreateTransaction;

namespace Budgetly.Api.Features.Goals.Contribute;

public sealed record ContributeToGoalRequest(decimal Amount, DateOnly Date);
public sealed record ContributeToGoalResponse(GoalResponse Goal, CreateTransactionResponse Transaction);
