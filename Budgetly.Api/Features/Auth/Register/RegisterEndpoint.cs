using System.ComponentModel.DataAnnotations;
using Budgetly.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth.Register;

public sealed record RegisterRequest(string? Email, string? Password);
public sealed record RegisterResponse(Guid Id, string Email);

public static class RegisterEndpoint
{
    public static void MapRegister(this WebApplication app) =>
        app.MapPost("/api/auth/register", HandleAsync).WithTags("Authentication").RequireRateLimiting("auth")
            .Produces<RegisterResponse>(201).ProducesValidationProblem().ProducesProblem(409);

    private static async Task<IResult> HandleAsync(RegisterRequest request, IPasswordHasher<BudgetlyUser> hasher,
        BudgetlyDbContext db, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (email is null || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Podaj poprawny adres e-mail."] });
        if (request.Password is not { Length: >= 8 and <= 128 })
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Hasło musi mieć od 8 do 128 znaków."] });

        var user = new BudgetlyUser { Email = email };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Results.Problem(title: "Konto z tym adresem już istnieje.", statusCode: 409);
        }
        return Results.Json(new RegisterResponse(user.Id, user.Email), statusCode: 201);
    }
}
