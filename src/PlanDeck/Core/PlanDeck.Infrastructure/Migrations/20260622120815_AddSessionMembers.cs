using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionMembers_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionMembers_SessionId",
                table: "SessionMembers",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMembers_TenantId",
                table: "SessionMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMembers_TenantId_SessionId_Email",
                table: "SessionMembers",
                columns: new[] { "TenantId", "SessionId", "Email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionMembers");
        }
    }
}
