using Budgetly.Api.Features.Auth;
using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Budgets;

public static class MonthlyBudgetEndpoints
{
    public static void MapMonthlySummary(this WebApplication app)
    {
        var group = app.MapGroup("/api/budgets").RequireAuthorization().WithTags("Budgets");
        group.MapGet("/{month}", async (string month, HttpContext context, MonthlyBudgetHandler handler, CancellationToken ct) =>
            (await handler.SummaryAsync(JwtTokens.UserId(context.User), month, ct)).ToHttpResult());
        group.MapDelete("/{month}", async (string month, HttpContext context, MonthlyBudgetHandler handler, CancellationToken ct) =>
            (await handler.DeleteAsync(JwtTokens.UserId(context.User), month, ct)).ToHttpResult(StatusCodes.Status204NoContent));
    }
}
