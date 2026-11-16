using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Goals;

public static class GoalEndpoints
{
    public sealed record GoalRequest(string? Name, decimal Target, decimal Saved);
    public sealed record GoalResponse(Guid Id, string Name, decimal Target, decimal Saved, DateTime? CompletedAtUtc);

    public static void MapGoals(this WebApplication app)
    {
        var group = app.MapGroup("/api/goals").RequireAuthorization().WithTags("Goals");
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:guid}", EditAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static Task<List<GoalResponse>> ListAsync(HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        var userId = JwtTokens.UserId(context.User);
        return db.Goals.Where(x => x.UserId == userId).OrderBy(x => x.Name)
            .Select(x => new GoalResponse(x.Id, x.Name, x.Target, x.Saved, x.CompletedAtUtc)).ToListAsync(ct);
    }

    private static Task<IResult> CreateAsync(GoalRequest request, HttpContext context, BudgetlyDbContext db, CancellationToken ct) =>
        SaveAsync(null, request, context, db, ct);

    private static Task<IResult> EditAsync(Guid id, GoalRequest request, HttpContext context, BudgetlyDbContext db, CancellationToken ct) =>
        SaveAsync(id, request, context, db, ct);

    private static async Task<IResult> SaveAsync(Guid? id, GoalRequest request, HttpContext context,
        BudgetlyDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 100 } || !BudgetPolicy.ValidMoney(request.Target) ||
            (request.Saved != 0 && !BudgetPolicy.ValidMoney(request.Saved)))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["goal"] = ["Podaj nazwę do 100 znaków, dodatni cel i nieujemną kwotę oszczędności."]
            });

        var userId = JwtTokens.UserId(context.User);
        var query = db.Goals.Where(x => x.Id == id && x.UserId == userId);
        if (id is null)
        {
            var goal = new SavingsGoal { UserId = userId, Name = name, Target = request.Target, Saved = request.Saved };
            db.Goals.Add(goal);
            await db.SaveChangesAsync(ct);
            return Results.Json(new GoalResponse(goal.Id, name, goal.Target, goal.Saved, null), statusCode: 201);
        }
        var count = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Name, name).SetProperty(x => x.Target, request.Target).SetProperty(x => x.Saved, request.Saved)
            .SetProperty(x => x.CompletedAtUtc, x => request.Saved >= request.Target ? x.CompletedAtUtc : null), ct);
        if (count == 0) return Results.NotFound();
        var response = await query.Select(x => new GoalResponse(x.Id, x.Name, x.Target, x.Saved, x.CompletedAtUtc))
            .SingleOrDefaultAsync(ct);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, BudgetlyDbContext db, CancellationToken ct)
    {
        var userId = JwtTokens.UserId(context.User);
        var count = await db.Goals.Where(x => x.Id == id && x.UserId == userId).ExecuteDeleteAsync(ct);
        return count == 0 ? Results.NotFound() : Results.NoContent();
    }
}
