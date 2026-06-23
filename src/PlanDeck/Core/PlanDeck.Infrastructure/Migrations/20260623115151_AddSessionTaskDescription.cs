using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTaskDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SessionTasks",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "SessionTasks");
        }
    }
}
