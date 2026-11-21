using Budgetly.Api.Features.Transactions.CreateTransaction;

namespace Budgetly.Api.Features.Transactions.Manage;

public sealed record TransactionPage(IReadOnlyList<CreateTransactionResponse> Items, int Total, int Page, int PageSize);
