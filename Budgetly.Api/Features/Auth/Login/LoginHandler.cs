using Budgetly.Api.Application;
using Budgetly.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth.Login;

public sealed class LoginHandler(IPasswordHasher<BudgetlyUser> hasher, JwtTokens tokens, BudgetlyDbContext db)
{
    public async Task<HandlerResult<LoginResponse>> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        if (request.Email is not { Length: > 0 and <= 254 } || request.Password is not { Length: > 0 and <= 128 })
            return HandlerError.Unauthorized();

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
            return HandlerError.Unauthorized();

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            return HandlerError.Unauthorized();
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        var (token, expires) = tokens.Issue(user);
        return HandlerResult<LoginResponse>.Success(new LoginResponse(token, expires, user.Email, user.Role));
    }
}
