using System.ComponentModel.DataAnnotations;
using Budgetly.Api.Features.Transactions;

namespace Budgetly.Api.Features.Transactions.CreateTransaction;

public sealed record CreateTransactionRequest(
    [property: Range(typeof(decimal), "0.01", "999999999.99")]
    decimal Amount,
    TransactionType Type,
    DateOnly Date,
    Guid CategoryId,
    [property: StringLength(250)]
    string? Description);
