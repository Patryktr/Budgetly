using Budgetly.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth;

public sealed class AdminAccountSeeder(
    IConfiguration configuration, BudgetlyDbContext db, IPasswordHasher<BudgetlyUser> hasher)
{
    public async Task SeedAsync()
    {
        var email = configuration["Admin:Email"]?.Trim().ToLowerInvariant();
        var password = configuration["Admin:Password"];
        if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(password))
            return;
        if (email is not { Length: > 0 and <= 254 } || password is not { Length: >= 12 and <= 128 })
            throw new InvalidOperationException("Set Admin__Email and a password between 12 and 128 characters.");

        var existing = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        if (existing is not null)
        {
            if (existing.Role != "ADMIN")
                throw new InvalidOperationException("The configured administrator email belongs to a regular user.");
            return;
        }

        var user = new BudgetlyUser { Email = email, Role = "ADMIN" };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

}
