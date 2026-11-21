using Budgetly.Api.Features.Auth;
using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Budgets.SetMonthlyBudget;

public static class SetMonthlyBudgetEndpoint
{
    public static void MapSetMonthlyBudgetEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/budgets/{month}", HandleAsync)
            .WithTags("Budgets")
            .RequireAuthorization()
            .Produces<SetMonthlyBudgetResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> HandleAsync(
        string month, SetMonthlyBudgetRequest request, HttpContext context,
        SetMonthlyBudgetHandler handler, CancellationToken ct) =>
        (await handler.HandleAsync(JwtTokens.UserId(context.User), month, request, ct)).ToHttpResult();
}
