using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Goals.Contribute;
using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Goals;

public static class GoalEndpoints
{
    public static void MapGoals(this WebApplication app)
    {
        var group = app.MapGroup("/api/goals").RequireAuthorization().WithTags("Goals");
        group.MapGet("", async (HttpContext context, GoalHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListAsync(JwtTokens.UserId(context.User), ct)));
        group.MapPost("", async (GoalRequest request, HttpContext context, GoalHandler handler, CancellationToken ct) =>
            (await handler.CreateAsync(JwtTokens.UserId(context.User), request, ct)).ToHttpResult(StatusCodes.Status201Created));
        group.MapPut("/{id:guid}", async (Guid id, GoalRequest request, HttpContext context, GoalHandler handler, CancellationToken ct) =>
            (await handler.EditAsync(JwtTokens.UserId(context.User), id, request, ct)).ToHttpResult());
        group.MapPost("/{id:guid}/contributions", async (Guid id, ContributeToGoalRequest request,
            HttpContext context, ContributeToGoalHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(JwtTokens.UserId(context.User), id, request, ct)).ToHttpResult(StatusCodes.Status201Created));
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext context, GoalHandler handler, CancellationToken ct) =>
            (await handler.DeleteAsync(JwtTokens.UserId(context.User), id, ct)).ToHttpResult(StatusCodes.Status204NoContent));
    }
}
