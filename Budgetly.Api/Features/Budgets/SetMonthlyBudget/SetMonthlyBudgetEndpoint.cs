using System.Data;
using System.Security.Claims;
using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;

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
        string month,
        SetMonthlyBudgetRequest request,
        HttpContext context,
        BudgetlyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        if (!BudgetPolicy.TryParseMonth(month, out var monthStart))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["month"] = ["Użyj formatu RRRR-MM."]
            });

        if (!BudgetPolicy.ValidMoney(request.Limit))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["Limit musi wynosić od 0,01 do 999 999 999,99 i mieć maksymalnie dwa miejsca po przecinku."]
            });

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var budget = await dbContext.MonthlyBudgets.SingleOrDefaultAsync(
            x => x.UserId == userId && x.MonthStart == monthStart, cancellationToken);

        if (budget is null)
        {
            budget = new MonthlyBudget(userId, monthStart);
            dbContext.MonthlyBudgets.Add(budget);
        }

        budget.SetLimit(request.Limit, request.HighPriority);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new SetMonthlyBudgetResponse(month, budget.Limit, budget.HighPriority));
    }
}
