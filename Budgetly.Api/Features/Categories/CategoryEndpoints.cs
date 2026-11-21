using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Categories;

public static class CategoryEndpoints
{
    public sealed record CategoryRequest(string? Name, TransactionType Type);
    public sealed record RenameCategoryRequest(string? Name);
    public sealed record CategoryResponse(Guid Id, string Name, TransactionType Type, bool CanEdit);

    public static void MapCategories(this WebApplication app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Categories");
        group.MapGet("", ListAsync);
        group.MapPost("", AddAsync);
        group.MapPut("/{id:guid}", RenameAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static Task<List<CategoryResponse>> ListAsync(HttpContext context, CategoryListCache cache, CancellationToken ct) =>
        cache.GetAsync(JwtTokens.UserId(context.User), ct);

    private static async Task<IResult> AddAsync(CategoryRequest request, HttpContext context, BudgetlyDbContext db,
        CategoryListCache cache, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 80 } || !Enum.IsDefined(request.Type))
            return Invalid();
        var userId = JwtTokens.UserId(context.User);
        var item = new Category { Name = name, Type = request.Type, UserId = userId };
        db.Categories.Add(item);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(userId);
        return Results.Json(new CategoryResponse(item.Id, item.Name, item.Type, true), statusCode: 201);
    }

    private static async Task<IResult> RenameAsync(Guid id, RenameCategoryRequest request, HttpContext context,
        BudgetlyDbContext db, CategoryListCache cache, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 80 }) return Invalid();
        var userId = JwtTokens.UserId(context.User);
        var item = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (item is null) return Results.NotFound();
        item.Name = name;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(userId);
        return Results.Ok(new CategoryResponse(item.Id, item.Name, item.Type, true));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, BudgetlyDbContext db,
        CategoryListCache cache, CancellationToken ct)
    {
        var userId = JwtTokens.UserId(context.User);
        var item = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (item is null) return Results.NotFound();
        db.Categories.Remove(item);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 547 })
        {
            return Results.Problem(title: "Kategoria jest używana przez transakcje.", statusCode: 409);
        }
        await cache.InvalidateAsync(userId);
        return Results.NoContent();
    }

    private static IResult Invalid() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["category"] = ["Podaj nazwę do 80 znaków i wybierz poprawny typ kategorii."]
    });
}
