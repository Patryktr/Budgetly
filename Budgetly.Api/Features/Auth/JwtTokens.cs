using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Budgetly.Api.Features.Auth;

public sealed class JwtTokens(IConfiguration configuration)
{
    public const string Issuer = "Budgetly";

    public static SymmetricSecurityKey SigningKey(IConfiguration configuration)
    {
        var secret = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Set Jwt__SigningKey to a random secret of at least 32 bytes.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public (string Token, DateTime ExpiresAtUtc) Issue(BudgetlyUser user)
    {
        var expires = DateTime.UtcNow.AddHours(1);
        Claim[] claims = [new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new("role", user.Role)];
        var token = new JwtSecurityToken(Issuer, Issuer, claims, expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(configuration), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static Guid UserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
