using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimix.Api.Migrations
{
    /// <inheritdoc />
    public partial class Hardening_20260703 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Dives_UserId_UpdatedAtUtc",
                table: "Dives");

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "SiteContributions",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "SiteContributions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Dives",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GearCsv",
                table: "Dives",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GasMix",
                table: "Dives",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConditionsJson",
                table: "Dives",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuddiesCsv",
                table: "Dives",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServerUpdatedAtUtc",
                table: "Dives",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "AnalyticsEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_SiteContributions_UserId",
                table: "SiteContributions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Dives_UserId_ServerUpdatedAtUtc",
                table: "Dives",
                columns: new[] { "UserId", "ServerUpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserPseudoId_EventId",
                table: "AnalyticsEvents",
                columns: new[] { "UserPseudoId", "EventId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SiteContributions_UserId",
                table: "SiteContributions");

            migrationBuilder.DropIndex(
                name: "IX_Dives_UserId_ServerUpdatedAtUtc",
                table: "Dives");

            migrationBuilder.DropIndex(
                name: "IX_AnalyticsEvents_UserPseudoId_EventId",
                table: "AnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "ServerUpdatedAtUtc",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "AnalyticsEvents");

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "SiteContributions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16384)",
                oldMaxLength: 16384);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "SiteContributions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Dives",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GearCsv",
                table: "Dives",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GasMix",
                table: "Dives",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConditionsJson",
                table: "Dives",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuddiesCsv",
                table: "Dives",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dives_UserId_UpdatedAtUtc",
                table: "Dives",
                columns: new[] { "UserId", "UpdatedAtUtc" });
        }
    }
}
