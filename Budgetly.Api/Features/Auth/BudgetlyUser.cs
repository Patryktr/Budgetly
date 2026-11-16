namespace Budgetly.Api.Features.Auth;

public sealed class BudgetlyUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; init; }
    public string PasswordHash { get; set; } = "";
    public string Role { get; init; } = "USER";
}
