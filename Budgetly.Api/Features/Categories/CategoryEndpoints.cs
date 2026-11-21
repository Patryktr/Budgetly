using Budgetly.Api.Features.Auth;
using Budgetly.Api.Infrastructure;

namespace Budgetly.Api.Features.Categories;

public static class CategoryEndpoints
{
    public static void MapCategories(this WebApplication app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Categories");
        group.MapGet("", async (HttpContext context, CategoryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListAsync(JwtTokens.UserId(context.User), ct)));
        group.MapPost("", async (CategoryRequest request, HttpContext context, CategoryHandler handler, CancellationToken ct) =>
            (await handler.AddAsync(JwtTokens.UserId(context.User), request, ct)).ToHttpResult(StatusCodes.Status201Created));
        group.MapPut("/{id:guid}", async (Guid id, RenameCategoryRequest request, HttpContext context, CategoryHandler handler, CancellationToken ct) =>
            (await handler.RenameAsync(JwtTokens.UserId(context.User), id, request, ct)).ToHttpResult());
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext context, CategoryHandler handler, CancellationToken ct) =>
            (await handler.DeleteAsync(JwtTokens.UserId(context.User), id, ct)).ToHttpResult(StatusCodes.Status204NoContent));
    }
}
