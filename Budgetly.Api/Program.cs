using Budgetly.Api.Data;
using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Auth.Login;
using Budgetly.Api.Features.Auth.Register;
using Budgetly.Api.Features.Budgets.SetMonthlyBudget;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Categories;
using Budgetly.Api.Features.Goals;
using Budgetly.Api.Features.Transactions.CreateTransaction;
using Budgetly.Api.Features.Transactions.Manage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<IPasswordHasher<BudgetlyUser>, PasswordHasher<BudgetlyUser>>();
builder.Services.AddSingleton<JwtTokens>();
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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddDbContext<BudgetlyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Budgetly")));
builder.Services.AddScoped<CreateTransactionHandler>();
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
await app.SeedAdminAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
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
