using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budgetly.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GoalContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                table: "Transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSavings",
                table: "Transactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Name", "Type", "UserId" },
                values: new object[] { new Guid("55555555-5555-4555-8555-555555555555"), "Oszczędności", "Expense", null });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_GoalId",
                table: "Transactions",
                column: "GoalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Goals_GoalId",
                table: "Transactions",
                column: "GoalId",
                principalTable: "Goals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Goals_GoalId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_GoalId",
                table: "Transactions");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-4555-8555-555555555555"));

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsSavings",
                table: "Transactions");
        }
    }
}
