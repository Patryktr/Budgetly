using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth.Login;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Budgetly.Tests;

public sealed class ApiTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)),
                ["Database:ApplyMigrations"] = "false",
                ["Goals:RunWorker"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<BudgetlyDbContext>>();
                services.RemoveAll<DbContextOptions<BudgetlyDbContext>>();
                services.AddDbContext<BudgetlyDbContext>(options => options.UseSqlite(connection));
            });
        });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>().Database.EnsureCreatedAsync();
        client = factory.CreateClient();
    }

    [Fact]
    public async Task RegistrationAndLoginProtectPrivateRoutesAndAdminRoute()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/transactions")).StatusCode);
        await SignInAsync(client, "user@example.test");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/summary")).StatusCode);
    }

    [Fact]
    public async Task RegularBudgetSavesExpenseAndReturnsWarning()
    {
        await SignInAsync(client, "regular@example.test");
        await SetBudgetAsync(false);
        var response = await client.PostAsJsonAsync("/api/transactions", Expense(110));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreateTransactionResponse>())!;
        Assert.Equal(-110m, created.Amount);
        Assert.Equal(110m, created.BudgetWarning?.ProjectedSpending);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        var summary = await client.GetFromJsonAsync<MonthlyBudgetEndpoints.MonthlySummary>("/api/budgets/2026-11");
        Assert.Equal(-110m, summary!.Balance);
        Assert.Equal(110m, Assert.Single(summary.Categories).Spending);
    }

    [Fact]
    public async Task HighPriorityBudgetRejectsExpenseWithoutSavingIt()
    {
        await SignInAsync(client, "strict@example.test");
        await SetBudgetAsync(true);
        var allowed = await client.PostAsJsonAsync("/api/transactions", Expense(70));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        var blocked = await client.PostAsJsonAsync("/api/transactions", Expense(31));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>().Transactions.CountAsync());
    }

    [Fact]
    public async Task UsersCannotReadUpdateOrDeleteAnotherUsersTransaction()
    {
        await SignInAsync(client, "owner@example.test");
        var created = await client.PostAsJsonAsync("/api/transactions", Expense(110));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var other = factory.CreateClient();
        await SignInAsync(other, "other@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync(created.Headers.Location, Expense(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(created.Headers.Location)).StatusCode);
    }

    private async Task SetBudgetAsync(bool highPriority)
    {
        var budget = await client.PutAsJsonAsync("/api/budgets/2026-11", new { limit = 100, highPriority });
        Assert.Equal(HttpStatusCode.OK, budget.StatusCode);
    }

    private static object Expense(decimal amount) => new
    {
        amount, type = 2, date = "2026-11-15", categoryId = "22222222-2222-4222-8222-222222222222"
    };

    private static async Task SignInAsync(HttpClient target, string email)
    {
        var credentials = new { email, password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) };
        var registration = await target.PostAsJsonAsync("/api/auth/register", credentials);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var login = await target.PostAsJsonAsync("/api/auth/login", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        target.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        await connection.DisposeAsync();
    }
}
