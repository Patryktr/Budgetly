namespace Budgetly.Api.Features.Goals;

public sealed record GoalRequest(string? Name, decimal Target, decimal? Saved = null);
public sealed record GoalResponse(Guid Id, string Name, decimal Target, decimal Saved, DateTime? CompletedAtUtc);
