using Budgetly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Features.Goals;

public sealed class CompleteGoalsWorker(IServiceScopeFactory scopeFactory, ILogger<CompleteGoalsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>();
                var now = DateTime.UtcNow;
                await db.Goals.Where(x => x.CompletedAtUtc == null && x.Saved >= x.Target)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CompletedAtUtc, now), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not mark completed savings goals. The worker will retry.");
            }
        }
    }
}
