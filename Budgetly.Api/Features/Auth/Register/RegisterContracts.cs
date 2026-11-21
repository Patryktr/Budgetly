namespace Budgetly.Api.Features.Auth.Register;

public sealed record RegisterRequest(string? Email, string? Password);
public sealed record RegisterResponse(Guid Id, string Email);
