using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimix.Api.Migrations
{
    /// <inheritdoc />
    public partial class AggregatorModelExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AscentRateMPerMin",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CeilingM",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Cns",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeartRate",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Ndl",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Setpoint",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TankPressureBar",
                table: "ProfileSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Current",
                table: "Dives",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryType",
                table: "Dives",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExposureSuit",
                table: "Dives",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Dives",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaterType",
                table: "Dives",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Weather",
                table: "Dives",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeightKg",
                table: "Dives",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cylinders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    O2 = table.Column<double>(type: "double precision", nullable: true),
                    He = table.Column<double>(type: "double precision", nullable: true),
                    StartBar = table.Column<double>(type: "double precision", nullable: true),
                    EndBar = table.Column<double>(type: "double precision", nullable: true),
                    VolumeL = table.Column<double>(type: "double precision", nullable: true),
                    WorkingPressureBar = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cylinders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cylinders_Dives_DiveId",
                        column: x => x.DiveId,
                        principalTable: "Dives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cylinders_DiveId_Ordinal",
                table: "Cylinders",
                columns: new[] { "DiveId", "Ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cylinders");

            migrationBuilder.DropColumn(
                name: "AscentRateMPerMin",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "CeilingM",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "Cns",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "HeartRate",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "Ndl",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "Setpoint",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "TankPressureBar",
                table: "ProfileSamples");

            migrationBuilder.DropColumn(
                name: "Current",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "EntryType",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "ExposureSuit",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "WaterType",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "Weather",
                table: "Dives");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Dives");
        }
    }
}
