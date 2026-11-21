using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Auth.Register;

public static class RegisterEndpoint
{
    public static void MapRegister(this WebApplication app) =>
        app.MapPost("/api/auth/register", HandleAsync)
            .WithTags("Authentication")
            .RequireRateLimiting("auth")
            .Produces<RegisterResponse>(201).ProducesValidationProblem().ProducesProblem(409);

    private static async Task<IResult> HandleAsync(RegisterRequest request, RegisterHandler handler, CancellationToken ct) =>
        (await handler.HandleAsync(request, ct)).ToHttpResult(StatusCodes.Status201Created);
}
