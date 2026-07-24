using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trimix.Api.Migrations
{
    /// <inheritdoc />
    public partial class WidenReceiptToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Receipts_Token",
                table: "Receipts");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "Receipts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AddColumn<string>(
                name: "TokenSha256",
                table: "Receipts",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_TokenSha256",
                table: "Receipts",
                column: "TokenSha256",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Receipts_TokenSha256",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "TokenSha256",
                table: "Receipts");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "Receipts",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_Token",
                table: "Receipts",
                column: "Token",
                unique: true);
        }
    }
}
