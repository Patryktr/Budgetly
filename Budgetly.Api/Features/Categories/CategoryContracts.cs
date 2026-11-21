using Budgetly.Api.Features.Transactions;

namespace Budgetly.Api.Features.Categories;

public sealed record CategoryRequest(string? Name, TransactionType Type);
public sealed record RenameCategoryRequest(string? Name);
public sealed record CategoryResponse(Guid Id, string Name, TransactionType Type, bool CanEdit);
