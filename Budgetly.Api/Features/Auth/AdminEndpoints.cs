using Budgetly.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth;

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        app.MapGet("/api/admin/summary", async (BudgetlyDbContext db, CancellationToken ct) =>
            new AdminSummary(await db.Users.CountAsync(ct)))
            .WithTags("Administration")
            .RequireAuthorization("Admin");
    }

    public static async Task SeedAdminAsync(this WebApplication app)
    {
        var email = app.Configuration["Admin:Email"]?.Trim().ToLowerInvariant();
        var password = app.Configuration["Admin:Password"];
        if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(password))
            return;
        if (email is not { Length: > 0 and <= 254 } || password is not { Length: >= 12 and <= 128 })
            throw new InvalidOperationException("Set Admin__Email and a password between 12 and 128 characters.");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>();
        var existing = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        if (existing is not null)
        {
            if (existing.Role != "ADMIN")
                throw new InvalidOperationException("The configured administrator email belongs to a regular user.");
            return;
        }

        var user = new BudgetlyUser { Email = email, Role = "ADMIN" };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<BudgetlyUser>>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public sealed record AdminSummary(int UserCount);
}
