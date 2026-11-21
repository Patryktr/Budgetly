using Budgetly.Api.Application;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Categories;

public sealed class CategoryHandler(BudgetlyDbContext db, CategoryListCache cache)
{
    public Task<List<CategoryResponse>> ListAsync(Guid userId, CancellationToken ct) =>
        cache.GetAsync(userId, ct);

    public async Task<HandlerResult<CategoryResponse>> AddAsync(Guid userId, CategoryRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 80 } || !Enum.IsDefined(request.Type))
            return Invalid();
        var item = new Category { Name = name, Type = request.Type, UserId = userId };
        db.Categories.Add(item);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(userId);
        return HandlerResult<CategoryResponse>.Success(new CategoryResponse(item.Id, item.Name, item.Type, true));
    }

    public async Task<HandlerResult<CategoryResponse>> RenameAsync(Guid userId, Guid id, RenameCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 80 }) return Invalid();
        var item = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (item is null) return HandlerError.NotFound();
        item.Name = name;
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(userId);
        return HandlerResult<CategoryResponse>.Success(new CategoryResponse(item.Id, item.Name, item.Type, true));
    }

    public async Task<HandlerResult<bool>> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var item = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (item is null) return HandlerError.NotFound();
        db.Categories.Remove(item);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 547 })
        {
            return HandlerError.Conflict("Kategoria jest używana przez transakcje.");
        }
        await cache.InvalidateAsync(userId);
        return HandlerResult<bool>.Success(true);
    }

    private static HandlerError Invalid() => HandlerError.Invalid(new Dictionary<string, string[]>
    {
        ["category"] = ["Podaj nazwę do 80 znaków i wybierz poprawny typ kategorii."]
    });
}
