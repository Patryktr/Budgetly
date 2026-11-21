using Budgetly.Api.Application;
using System.ComponentModel.DataAnnotations;
using Budgetly.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Auth.Register;

public sealed class RegisterHandler(IPasswordHasher<BudgetlyUser> hasher, BudgetlyDbContext db)
{
    public async Task<HandlerResult<RegisterResponse>> HandleAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (email is null || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return HandlerError.Invalid(new Dictionary<string, string[]> { ["email"] = ["Podaj poprawny adres e-mail."] });
        if (request.Password is not { Length: >= 8 and <= 128 })
            return HandlerError.Invalid(new Dictionary<string, string[]> { ["password"] = ["Hasło musi mieć od 8 do 128 znaków."] });

        var user = new BudgetlyUser { Email = email };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return HandlerError.Conflict("Konto z tym adresem już istnieje.");
        }
        return HandlerResult<RegisterResponse>.Success(new RegisterResponse(user.Id, user.Email));
    }
}
