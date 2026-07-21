using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAzureDevOpsConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectAzureDevOpsConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AzureDevOpsProject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EstimateField = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DescriptionField = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReproStepsField = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AcceptanceCriteriaField = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SecretName = table.Column<string>(type: "nvarchar(127)", maxLength: 127, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ValidationState = table.Column<int>(type: "int", nullable: false),
                    LastValidatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TargetLockedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAzureDevOpsConnections", x => x.Id);
                    table.CheckConstraint("CK_ProjectAzureDevOpsConnections_ValidationState", "[ValidationState] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_ProjectAzureDevOpsConnections_Projects_TenantId_ProjectId",
                        columns: x => new { x.TenantId, x.ProjectId },
                        principalTable: "Projects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAzureDevOpsConnections_TenantId_ProjectId",
                table: "ProjectAzureDevOpsConnections",
                columns: new[] { "TenantId", "ProjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectAzureDevOpsConnections");
        }
    }
}
