namespace Budgetly.Api.Features.Auth.Login;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string Email, string Role);
