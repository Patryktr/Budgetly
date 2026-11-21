using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth.Login;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Categories;
using Budgetly.Api.Features.Goals;
using Budgetly.Api.Features.Goals.Contribute;
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
        var summary = await client.GetFromJsonAsync<MonthlySummary>("/api/budgets/2026-11");
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

    [Fact]
    public async Task CategoryHandlersPreserveValidationCacheInvalidationAndOwnership()
    {
        await SignInAsync(client, "categories@example.test");
        var initial = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/categories", new { name = "", type = 2 })).StatusCode);

        var created = await client.PostAsJsonAsync("/api/categories", new { name = "Weekend", type = 2 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var category = (await created.Content.ReadFromJsonAsync<CategoryResponse>())!;
        var ownList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        Assert.Equal(initial!.Count + 1, ownList!.Count);
        Assert.Contains(ownList, x => x.Id == category.Id);

        using var other = factory.CreateClient();
        await SignInAsync(other, "other-category@example.test");
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync("/api/categories/" + category.Id, new { name = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync("/api/categories/" + category.Id)).StatusCode);
        Assert.DoesNotContain((await other.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"))!,
            x => x.Id == category.Id);

        var renamed = await client.PutAsJsonAsync("/api/categories/" + category.Id, new { name = "Trip" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Trip", (await renamed.Content.ReadFromJsonAsync<CategoryResponse>())!.Name);
        Assert.Contains((await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"))!,
            x => x.Id == category.Id && x.Name == "Trip");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/categories/" + category.Id)).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"))!,
            x => x.Id == category.Id);
    }

    [Fact]
    public async Task GoalAndBudgetHandlersPreserveResponsesAndPrivateData()
    {
        await SignInAsync(client, "goals@example.test");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/budgets/invalid", new { limit = 50 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/goals", new { name = "Trip", target = 0, saved = 0 })).StatusCode);
        var created = await client.PostAsJsonAsync("/api/goals", new { name = "Trip", target = 100 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var goal = (await created.Content.ReadFromJsonAsync<GoalResponse>())!;
        Assert.Equal(0m, goal.Saved);

        using var other = factory.CreateClient();
        await SignInAsync(other, "other-goals@example.test");
        Assert.Empty((await other.GetFromJsonAsync<List<GoalResponse>>("/api/goals"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync("/api/goals/" + goal.Id, new { name = "Trip", target = 100, saved = 40 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync("/api/goals/" + goal.Id)).StatusCode);

        var edited = await client.PutAsJsonAsync("/api/goals/" + goal.Id, new { name = "Travel", target = 100 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Travel", (await edited.Content.ReadFromJsonAsync<GoalResponse>())!.Name);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/goals/" + goal.Id)).StatusCode);
        await SetBudgetAsync(true);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/budgets/2026-11")).StatusCode);
        var summary = await client.GetFromJsonAsync<MonthlySummary>("/api/budgets/2026-11");
        Assert.Null(summary!.Limit);
    }

    [Fact]
    public async Task GoalContributionReducesBalanceOutsideStrictExpenseLimitAndAllowsOverfunding()
    {
        await SignInAsync(client, "funding@example.test");
        await SetBudgetAsync(true);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/transactions", Expense(100))).StatusCode);
        var goal = await CreateGoalAsync(50);
        var response = await client.PostAsJsonAsync($"/api/goals/{goal.Id}/contributions", new { amount = 60, date = "2026-11-15" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var contribution = (await response.Content.ReadFromJsonAsync<ContributeToGoalResponse>())!;
        Assert.Equal(60m, contribution.Goal.Saved);
        Assert.Equal(-60m, contribution.Transaction.Amount);
        Assert.True(contribution.Transaction.IsSavings);
        Assert.Equal(goal.Id, contribution.Transaction.GoalId);
        var summary = (await client.GetFromJsonAsync<MonthlySummary>("/api/budgets/2026-11"))!;
        Assert.Equal(100m, summary.Spending);
        Assert.Equal(60m, summary.Savings);
        Assert.Equal(-160m, summary.Balance);
        Assert.Equal(100m, Assert.Single(summary.Categories).Spending);
        Assert.Equal(100m, summary.SpendingTrend.Single(x => x.Month == "2026-11").Spending);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transactions", Expense(0.01m))).StatusCode);
    }

    [Fact]
    public async Task SavingsTransfersDoNotUseExpenseAllowanceWhenOrdinaryExpenseIsEdited()
    {
        await SignInAsync(client, "allowance@example.test");
        await SetBudgetAsync(true);
        var goal = await CreateGoalAsync(1000);
        await ContributeAsync(goal.Id, 500);
        var expense = await client.PostAsJsonAsync("/api/transactions", Expense(90));
        Assert.Equal(HttpStatusCode.Created, expense.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(expense.Headers.Location, Expense(100))).StatusCode);
        var summary = (await client.GetFromJsonAsync<MonthlySummary>("/api/budgets/2026-11"))!;
        Assert.Equal(100m, summary.Spending);
        Assert.Equal(500m, summary.Savings);
    }

    [Fact]
    public async Task EditingAndDeletingContributionUpdatesGoalAndCompletionAtomically()
    {
        await SignInAsync(client, "adjust-funding@example.test");
        var goal = await CreateGoalAsync(100);
        var contribution = await ContributeAsync(goal.Id, 100);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CompleteGoalsHandler>().HandleAsync(default));
        Assert.NotNull(Assert.Single((await client.GetFromJsonAsync<List<GoalResponse>>("/api/goals"))!).CompletedAtUtc);
        var id = contribution.Transaction.Id;
        var corrected = await client.PutAsJsonAsync("/api/transactions/" + id, new
        {
            amount = 40, type = 2, date = "2026-11-16", categoryId = SavingsGoal.CategoryId, description = "Korekta wpłaty"
        });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var saved = Assert.Single((await client.GetFromJsonAsync<List<GoalResponse>>("/api/goals"))!);
        Assert.Equal(40m, saved.Saved);
        Assert.Null(saved.CompletedAtUtc);
        var invalid = await client.PutAsJsonAsync("/api/transactions/" + id, new
        {
            amount = 40, type = 1, date = "2026-11-16", categoryId = "11111111-1111-4111-8111-111111111111"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/transactions/" + id)).StatusCode);
        Assert.Equal(0m, Assert.Single((await client.GetFromJsonAsync<List<GoalResponse>>("/api/goals"))!).Saved);
        var summary = (await client.GetFromJsonAsync<MonthlySummary>("/api/budgets/2026-11"))!;
        Assert.Equal(0m, summary.Savings);
        Assert.Equal(0m, summary.Balance);
    }

    [Fact]
    public async Task DeletingGoalPreservesTransfersAndTheirExclusionFromExpenseBudget()
    {
        await SignInAsync(client, "delete-funded-goal@example.test");
        var goal = await CreateGoalAsync(100);
        var contribution = await ContributeAsync(goal.Id, 60);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/goals/" + goal.Id)).StatusCode);
        var transfer = (await client.GetFromJsonAsync<CreateTransactionResponse>("/api/transactions/" + contribution.Transaction.Id))!;
        Assert.Null(transfer.GoalId);
        Assert.True(transfer.IsSavings);
        Assert.Equal(-60m, transfer.Amount);
        await SetBudgetAsync(true);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/transactions", Expense(100))).StatusCode);
        var summary = (await client.GetFromJsonAsync<MonthlySummary>("/api/budgets/2026-11"))!;
        Assert.Equal(100m, summary.Spending);
        Assert.Equal(60m, summary.Savings);
        Assert.Equal(-160m, summary.Balance);
    }

    [Fact]
    public async Task ContributionsValidateOwnershipAmountAndDateWithoutChangingData()
    {
        await SignInAsync(client, "private-funding@example.test");
        var goal = await CreateGoalAsync(100);
        using var other = factory.CreateClient();
        await SignInAsync(other, "other-funding@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/goals/{goal.Id}/contributions",
            new { amount = 10, date = "2026-11-15" })).StatusCode);
        foreach (var amount in new[] { 0m, -1m, 1.001m })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/goals/{goal.Id}/contributions",
                new { amount, date = "2026-11-15" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/goals/{goal.Id}/contributions",
            new { amount = 10 })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>();
        Assert.Empty(await db.Transactions.ToListAsync());
        Assert.Equal(0m, (await db.Goals.SingleAsync()).Saved);
    }

    [Fact]
    public async Task GoalSavingsCannotBeChangedDirectlyAndOverflowLeavesNoExtraTransfer()
    {
        await SignInAsync(client, "funding-validation@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/goals",
            new { name = "Trip", target = 100, saved = 50 })).StatusCode);
        var goal = await CreateGoalAsync(100);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/goals/" + goal.Id,
            new { name = "Trip", target = 100, saved = 50 })).StatusCode);
        await ContributeAsync(goal.Id, 999999999.99m);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/goals/{goal.Id}/contributions",
            new { amount = 0.01, date = "2026-11-15" })).StatusCode);
        var saved = Assert.Single((await client.GetFromJsonAsync<List<GoalResponse>>("/api/goals"))!);
        Assert.Equal(999999999.99m, saved.Saved);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>().Transactions.CountAsync());
    }

    private async Task<GoalResponse> CreateGoalAsync(decimal target)
    {
        var response = await client.PostAsJsonAsync("/api/goals", new { name = "Trip", target });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GoalResponse>())!;
    }

    private async Task<ContributeToGoalResponse> ContributeAsync(Guid goalId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/goals/{goalId}/contributions", new { amount, date = "2026-11-15" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ContributeToGoalResponse>())!;
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
