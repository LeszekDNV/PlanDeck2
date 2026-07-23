using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanDeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStableAppUserIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_TenantId",
                table: "AppUsers");

            migrationBuilder.AddColumn<Guid>(
                name: "EntraObjectId",
                table: "AppUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "AppUsers",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.Sql(
                """
                EXEC(N'
                    UPDATE [AppUsers]
                    SET [EntraObjectId] = [Id],
                        [NormalizedEmail] = UPPER(LTRIM(RTRIM([Email])));
                ');
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "EntraObjectId",
                table: "AppUsers",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "AppUsers",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_EntraObjectId",
                table: "AppUsers",
                columns: new[] { "TenantId", "EntraObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_NormalizedEmail",
                table: "AppUsers",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_TenantId_EntraObjectId",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_TenantId_NormalizedEmail",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EntraObjectId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "AppUsers");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId",
                table: "AppUsers",
                column: "TenantId");
        }
    }
}
