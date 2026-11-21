namespace Budgetly.Api.Features.Transactions;

public sealed class Transaction
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public Guid? GoalId { get; private set; }
    public bool IsSavings { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Description { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private Transaction() { }

    public void Update(Guid categoryId, decimal amount, TransactionType type, DateOnly date, string? description)
    {
        CategoryId = categoryId;
        Amount = SignedAmount(amount, type);
        Date = date;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public static Transaction Create(
        Guid userId,
        Guid categoryId,
        decimal amount,
        TransactionType type,
        DateOnly date,
        string? description,
        Guid? transactionId = null,
        Guid? goalId = null)
    {
        var transaction = new Transaction { UserId = userId, Id = transactionId ?? Guid.NewGuid(), GoalId = goalId, IsSavings = goalId is not null };
        transaction.Update(categoryId, amount, type, date, description);
        return transaction;
    }

    public static decimal SignedAmount(decimal amount, TransactionType type)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Kwota musi być większa od zera.");

        return type switch
        {
            TransactionType.Income => amount,
            TransactionType.Expense => -amount,
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Nieznany typ transakcji.")
        };

    }
}
