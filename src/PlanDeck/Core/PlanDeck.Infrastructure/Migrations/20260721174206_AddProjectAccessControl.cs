using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SessionMembers_Sessions_SessionId",
                table: "SessionMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_SessionTasks_Sessions_SessionId",
                table: "SessionTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamMembers_Teams_TeamId",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_Teams_TenantId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_TeamId",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_TenantId",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_TenantId_TeamId_Email",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_SessionTasks_SessionId",
                table: "SessionTasks");

            migrationBuilder.DropIndex(
                name: "IX_SessionTasks_TenantId",
                table: "SessionTasks");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_TenantId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_SessionMembers_SessionId",
                table: "SessionMembers");

            migrationBuilder.DropIndex(
                name: "IX_SessionMembers_TenantId",
                table: "SessionMembers");

            migrationBuilder.Sql("""
                DELETE FROM [SessionMembers];
                DELETE FROM [SessionTasks];
                DELETE FROM [Sessions];
                """);

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Sessions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcceptedAtUtc",
                table: "TeamMembers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AppUserId",
                table: "TeamMembers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "TeamMembers",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TeamMembers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "Sessions",
                type: "uniqueidentifier",
                nullable: false);

            migrationBuilder.Sql("""
                UPDATE [TeamMembers]
                SET [NormalizedEmail] = UPPER(LTRIM(RTRIM([Email])));
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Teams_TenantId_Id",
                table: "Teams",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Sessions_TenantId_Id",
                table: "Sessions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppUsers_TenantId_Id",
                table: "AppUsers",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.UniqueConstraint("AK_Projects_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ProjectMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMembers", x => x.Id);
                    table.CheckConstraint("CK_ProjectMembers_Resolution", "([Status] = 0 AND [AppUserId] IS NULL AND [AcceptedAtUtc] IS NULL) OR ([Status] = 1 AND [AppUserId] IS NOT NULL AND [AcceptedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ProjectMembers_Role", "[Role] IN (1, 2, 3)");
                    table.CheckConstraint("CK_ProjectMembers_Status", "[Status] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_ProjectMembers_AppUsers_TenantId_AppUserId",
                        columns: x => new { x.TenantId, x.AppUserId },
                        principalTable: "AppUsers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Projects_TenantId_ProjectId",
                        columns: x => new { x.TenantId, x.ProjectId },
                        principalTable: "Projects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTeams_Projects_TenantId_ProjectId",
                        columns: x => new { x.TenantId, x.ProjectId },
                        principalTable: "Projects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTeams_Teams_TenantId_TeamId",
                        columns: x => new { x.TenantId, x.TeamId },
                        principalTable: "Teams",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId_AppUserId",
                table: "TeamMembers",
                columns: new[] { "TenantId", "AppUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId_TeamId_AppUserId",
                table: "TeamMembers",
                columns: new[] { "TenantId", "TeamId", "AppUserId" },
                unique: true,
                filter: "[AppUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId_TeamId_NormalizedEmail",
                table: "TeamMembers",
                columns: new[] { "TenantId", "TeamId", "NormalizedEmail" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SessionTasks_TenantId_SessionId",
                table: "SessionTasks",
                columns: new[] { "TenantId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TenantId_ProjectId_CreatedAtUtc",
                table: "Sessions",
                columns: new[] { "TenantId", "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_TenantId_AppUserId",
                table: "ProjectMembers",
                columns: new[] { "TenantId", "AppUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_TenantId_ProjectId",
                table: "ProjectMembers",
                columns: new[] { "TenantId", "ProjectId" },
                unique: true,
                filter: "[Role] = 3 AND [Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_TenantId_ProjectId_AppUserId",
                table: "ProjectMembers",
                columns: new[] { "TenantId", "ProjectId", "AppUserId" },
                unique: true,
                filter: "[AppUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_TenantId_ProjectId_NormalizedEmail",
                table: "ProjectMembers",
                columns: new[] { "TenantId", "ProjectId", "NormalizedEmail" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_Name",
                table: "Projects",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTeams_TenantId_ProjectId_TeamId",
                table: "ProjectTeams",
                columns: new[] { "TenantId", "ProjectId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTeams_TenantId_TeamId",
                table: "ProjectTeams",
                columns: new[] { "TenantId", "TeamId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SessionMembers_Sessions_TenantId_SessionId",
                table: "SessionMembers",
                columns: new[] { "TenantId", "SessionId" },
                principalTable: "Sessions",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Projects_TenantId_ProjectId",
                table: "Sessions",
                columns: new[] { "TenantId", "ProjectId" },
                principalTable: "Projects",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SessionTasks_Sessions_TenantId_SessionId",
                table: "SessionTasks",
                columns: new[] { "TenantId", "SessionId" },
                principalTable: "Sessions",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamMembers_AppUsers_TenantId_AppUserId",
                table: "TeamMembers",
                columns: new[] { "TenantId", "AppUserId" },
                principalTable: "AppUsers",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamMembers_Teams_TenantId_TeamId",
                table: "TeamMembers",
                columns: new[] { "TenantId", "TeamId" },
                principalTable: "Teams",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This restores only the previous schema shape. Session data deleted by Up can be
            // recovered only from a backup.
            migrationBuilder.DropForeignKey(
                name: "FK_SessionMembers_Sessions_TenantId_SessionId",
                table: "SessionMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Projects_TenantId_ProjectId",
                table: "Sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_SessionTasks_Sessions_TenantId_SessionId",
                table: "SessionTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamMembers_AppUsers_TenantId_AppUserId",
                table: "TeamMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamMembers_Teams_TenantId_TeamId",
                table: "TeamMembers");

            migrationBuilder.DropTable(
                name: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "ProjectTeams");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Teams_TenantId_Id",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_TenantId_AppUserId",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_TenantId_TeamId_AppUserId",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_TenantId_TeamId_NormalizedEmail",
                table: "TeamMembers");

            migrationBuilder.DropIndex(
                name: "IX_SessionTasks_TenantId_SessionId",
                table: "SessionTasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Sessions_TenantId_Id",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_TenantId_ProjectId_CreatedAtUtc",
                table: "Sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppUsers_TenantId_Id",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AcceptedAtUtc",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Sessions");

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "Sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TenantId",
                table: "Teams",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId",
                table: "TeamMembers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId",
                table: "TeamMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId_TeamId_Email",
                table: "TeamMembers",
                columns: new[] { "TenantId", "TeamId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionTasks_SessionId",
                table: "SessionTasks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionTasks_TenantId",
                table: "SessionTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TenantId",
                table: "Sessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMembers_SessionId",
                table: "SessionMembers",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMembers_TenantId",
                table: "SessionMembers",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_SessionMembers_Sessions_SessionId",
                table: "SessionMembers",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SessionTasks_Sessions_SessionId",
                table: "SessionTasks",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamMembers_Teams_TeamId",
                table: "TeamMembers",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
