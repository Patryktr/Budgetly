namespace Budgetly.Api.Features.Budgets;

public sealed class MonthlyBudget
{
    private MonthlyBudget() { }

    public MonthlyBudget(Guid userId, DateOnly monthStart)
    {
        UserId = userId;
        MonthStart = monthStart;
    }

    public Guid UserId { get; private set; }
    public DateOnly MonthStart { get; private set; }
    public decimal Limit { get; private set; }
    public bool HighPriority { get; private set; }

    public void SetLimit(decimal limit, bool highPriority)
    {
        Limit = limit;
        HighPriority = highPriority;
    }
}
