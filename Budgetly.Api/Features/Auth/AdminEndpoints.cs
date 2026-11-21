namespace Budgetly.Api.Features.Auth;

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app) =>
        app.MapGet("/api/admin/summary", async (AdminSummaryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
            .WithTags("Administration")
            .RequireAuthorization("Admin");
}
