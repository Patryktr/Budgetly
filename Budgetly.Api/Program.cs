using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Auth.Login;
using Budgetly.Api.Features.Auth.Register;
using Budgetly.Api.Features.Budgets.SetMonthlyBudget;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Categories;
using Budgetly.Api.Features.Goals;
using Budgetly.Api.Features.Goals.Contribute;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Budgetly.Api.Features.Transactions.Manage;
using Budgetly.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<IPasswordHasher<BudgetlyUser>, PasswordHasher<BudgetlyUser>>();
builder.Services.AddSingleton<JwtTokens>();
builder.Services.AddScoped<RegisterHandler>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<AdminSummaryHandler>();
builder.Services.AddScoped<AdminAccountSeeder>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = JwtTokens.Issuer,
        ValidAudience = JwtTokens.Issuer,
        IssuerSigningKey = JwtTokens.SigningKey(builder.Configuration),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuerSigningKey = true,
        RoleClaimType = "role"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            if (!Guid.TryParse(identity.FindFirst("sub")?.Value, out var id) || id == Guid.Empty)
                context.Fail("Invalid user identifier.");
            else
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("ADMIN"));
builder.Services.AddRateLimiter(RequestLimits.Configure);
var redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redis))
    builder.Services.AddDistributedMemoryCache();
else
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redis;
        options.InstanceName = "budgetly:";
    });
}
builder.Services.AddScoped<CategoryListCache>();
builder.Services.AddDbContext<BudgetlyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Budgetly")));
builder.Services.AddScoped<CreateTransactionHandler>();
builder.Services.AddScoped<TransactionHandler>();
builder.Services.AddScoped<CategoryHandler>();
builder.Services.AddScoped<GoalHandler>();
builder.Services.AddScoped<ContributeToGoalHandler>();
builder.Services.AddScoped<MonthlyBudgetHandler>();
builder.Services.AddScoped<SetMonthlyBudgetHandler>();
builder.Services.AddScoped<CompleteGoalsHandler>();
if (builder.Configuration.GetValue("Goals:RunWorker", true))
    builder.Services.AddHostedService<CompleteGoalsWorker>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BudgetlyDbContext>().Database.MigrateAsync();
}
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<AdminAccountSeeder>().SeedAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapRegister();
app.MapLogin();
app.MapAdmin();
app.MapCreateTransactionEndpoint();
app.MapSetMonthlyBudgetEndpoint();
app.MapTransactionQueries();
app.MapCategories();
app.MapMonthlySummary();
app.MapGoals();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program { }
