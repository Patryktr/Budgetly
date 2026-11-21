using System.Text.Json;
using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Budgetly.Api.Features.Categories;

public sealed class CategoryListCache(
    BudgetlyDbContext db, IDistributedCache cache, ILogger<CategoryListCache> logger)
{
    private static string Key(Guid userId) => $"categories:v2:{userId:N}";

    public async Task<List<CategoryResponse>> GetAsync(Guid userId, CancellationToken ct)
    {
        var key = Key(userId);
        var cacheAvailable = true;
        try
        {
            var payload = await cache.GetAsync(key, ct);
            if (payload is not null && JsonSerializer.Deserialize<List<CategoryResponse>>(payload) is { } cached)
                return cached;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            cacheAvailable = false;
            logger.LogWarning(ex, "Category cache unavailable. Reading from SQL Server.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Ignoring invalid category cache entry.");
        }

        var categories = await db.Categories.AsNoTracking()
            .Where(x => x.UserId == null || x.UserId == userId).OrderBy(x => x.Name)
            .Select(x => new CategoryResponse(x.Id, x.Name, x.Type, x.UserId != null)).ToListAsync(ct);

        if (cacheAvailable)
        {
            try
            {
                await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(categories),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) }, ct);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                logger.LogWarning(ex, "Could not refresh category cache.");
            }
        }

        return categories;
    }

    public async Task InvalidateAsync(Guid userId)
    {
        try
        {
            await cache.RemoveAsync(Key(userId));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not invalidate category cache. Entry will expire.");
        }
    }
}
