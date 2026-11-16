namespace Budgetly.Api.Features.Goals;

public sealed class SavingsGoal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Name { get; set; } = "";
    public decimal Target { get; set; }
    public decimal Saved { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
