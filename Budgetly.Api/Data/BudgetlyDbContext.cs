using Budgetly.Api.Features.Auth;
using Budgetly.Api.Features.Budgets;
using Budgetly.Api.Features.Goals;
using Budgetly.Api.Features.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Budgetly.Api.Data;

public sealed class BudgetlyDbContext(DbContextOptions<BudgetlyDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<BudgetlyUser> Users => Set<BudgetlyUser>();
    public DbSet<SavingsGoal> Goals => Set<SavingsGoal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BudgetlyUser>(entity =>
        {
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Role).HasMaxLength(10);
        });

        modelBuilder.Entity<SavingsGoal>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Target).HasPrecision(12, 2);
            entity.Property(x => x.Saved).HasPrecision(12, 2);
            entity.HasOne<BudgetlyUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MonthlyBudget>(entity =>
        {
            entity.ToTable("MonthlyBudgets");
            entity.HasKey(x => new { x.UserId, x.MonthStart });
            entity.Property(x => x.Limit).HasPrecision(12, 2);
            entity.HasOne<BudgetlyUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(12, 2);
            entity.Property(x => x.Description).HasMaxLength(250);
            entity.HasIndex(x => new { x.UserId, x.Date });
            entity.HasOne<BudgetlyUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SavingsGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            entity.HasOne<BudgetlyUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasData(
                new { Id = Guid.Parse("11111111-1111-4111-8111-111111111111"), Name = "Wynagrodzenie", Type = TransactionType.Income },
                new { Id = Guid.Parse("22222222-2222-4222-8222-222222222222"), Name = "Zakupy spożywcze", Type = TransactionType.Expense },
                new { Id = Guid.Parse("33333333-3333-4333-8333-333333333333"), Name = "Transport", Type = TransactionType.Expense },
                new { Id = Guid.Parse("44444444-4444-4444-8444-444444444444"), Name = "Mieszkanie", Type = TransactionType.Expense },
                new { Id = SavingsGoal.CategoryId, Name = "Oszczędności", Type = TransactionType.Expense });
        });
    }
}
