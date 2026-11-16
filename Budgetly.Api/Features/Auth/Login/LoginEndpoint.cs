using Budgetly.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth.Login;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string Email, string Role);

public static class LoginEndpoint
{
    public static void MapLogin(this WebApplication app) =>
        app.MapPost("/api/auth/login", HandleAsync).WithTags("Authentication").RequireRateLimiting("auth")
            .Produces<LoginResponse>().Produces(401);

    private static async Task<IResult> HandleAsync(LoginRequest request, IPasswordHasher<BudgetlyUser> hasher,
        JwtTokens tokens, BudgetlyDbContext db, CancellationToken ct)
    {
        if (request.Email is not { Length: > 0 and <= 254 } || request.Password is not { Length: > 0 and <= 128 })
            return Results.Unauthorized();

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
            return Results.Unauthorized();

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            return Results.Unauthorized();
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        var (token, expires) = tokens.Issue(user);
        return Results.Ok(new LoginResponse(token, expires, user.Email, user.Role));
    }
}
