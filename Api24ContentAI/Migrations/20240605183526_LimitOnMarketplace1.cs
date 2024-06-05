using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class LimitOnMarketplace1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Limit",
                schema: "ContentDb",
                table: "Marketplaces",
                newName: "TranslateLimit");

            migrationBuilder.AddColumn<int>(
                name: "ContentLimit",
                schema: "ContentDb",
                table: "Marketplaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentLimit",
                schema: "ContentDb",
                table: "Marketplaces");

            migrationBuilder.RenameColumn(
                name: "TranslateLimit",
                schema: "ContentDb",
                table: "Marketplaces",
                newName: "Limit");
        }
    }
}
