using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionShareCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareCode",
                table: "Sessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ShareCode",
                table: "Sessions",
                column: "ShareCode",
                unique: true,
                filter: "[ShareCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_ShareCode",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ShareCode",
                table: "Sessions");
        }
    }
}
