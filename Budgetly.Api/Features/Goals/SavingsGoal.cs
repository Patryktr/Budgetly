namespace Budgetly.Api.Features.Goals;

public sealed class SavingsGoal
{
    public static readonly Guid CategoryId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Name { get; set; } = "";
    public decimal Target { get; set; }
    public decimal Saved { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public void AdjustSaved(decimal difference)
    {
        Saved += difference;
        if (Saved < Target) CompletedAtUtc = null;
    }
}
