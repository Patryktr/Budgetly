using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Auth.Login;

public static class LoginEndpoint
{
    public static void MapLogin(this WebApplication app) =>
        app.MapPost("/api/auth/login", HandleAsync)
            .WithTags("Authentication")
            .RequireRateLimiting("auth")
            .Produces<LoginResponse>().Produces(401);

    private static async Task<IResult> HandleAsync(LoginRequest request, LoginHandler handler, CancellationToken ct) =>
        (await handler.HandleAsync(request, ct)).ToHttpResult();
}
