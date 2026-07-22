using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CascadeProjectSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Projects_TenantId_ProjectId",
                table: "Sessions");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Projects_TenantId_ProjectId",
                table: "Sessions",
                columns: new[] { "TenantId", "ProjectId" },
                principalTable: "Projects",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Projects_TenantId_ProjectId",
                table: "Sessions");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Projects_TenantId_ProjectId",
                table: "Sessions",
                columns: new[] { "TenantId", "ProjectId" },
                principalTable: "Projects",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
