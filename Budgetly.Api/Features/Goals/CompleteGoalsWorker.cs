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
                await scope.ServiceProvider.GetRequiredService<CompleteGoalsHandler>().HandleAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not mark completed savings goals. The worker will retry.");
            }
        }
    }
}
