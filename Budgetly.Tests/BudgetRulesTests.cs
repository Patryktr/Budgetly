using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Transactions;

namespace Budgetly.Tests;

public sealed class BudgetRulesTests
{
    [Fact]
    public void IncomeIsPositive() => Assert.Equal(32.25m, Transaction.SignedAmount(32.25m, TransactionType.Income));

    [Fact]
    public void ExpenseIsNegative() => Assert.Equal(-32.25m, Transaction.SignedAmount(32.25m, TransactionType.Expense));

    [Fact]
    public void ExactlyReachingLimitIsAllowed() => Assert.False(BudgetPolicy.IsOverLimit(500, 480, 20));

    [Fact]
    public void ExceedingLimitByOneCentIsDetected() => Assert.True(BudgetPolicy.IsOverLimit(500, 480, 20.01m));

    [Fact]
    public void FractionOfACentIsRejected() => Assert.False(BudgetPolicy.ValidMoney(12.345m));
}
