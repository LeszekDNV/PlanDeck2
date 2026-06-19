using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTaskAdoUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SessionTasks_SessionId_AdoWorkItemId",
                table: "SessionTasks",
                columns: new[] { "SessionId", "AdoWorkItemId" },
                unique: true,
                filter: "[AdoWorkItemId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionTasks_SessionId_AdoWorkItemId",
                table: "SessionTasks");
        }
    }
}
