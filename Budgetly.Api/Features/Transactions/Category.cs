namespace Budgetly.Api.Features.Transactions;

public sealed class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; }
    public string Name { get; set; } = "";
    public TransactionType Type { get; init; }
}
