using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class initialDb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ContentDb");

            migrationBuilder.RenameTable(
                name: "Templates",
                newName: "Templates",
                newSchema: "ContentDb");

            migrationBuilder.RenameTable(
                name: "ProductCategories",
                newName: "ProductCategories",
                newSchema: "ContentDb");

            migrationBuilder.RenameTable(
                name: "Marketplaces",
                newName: "Marketplaces",
                newSchema: "ContentDb");

            migrationBuilder.RenameTable(
                name: "CustomTemplates",
                newName: "CustomTemplates",
                newSchema: "ContentDb");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Templates",
                schema: "ContentDb",
                newName: "Templates");

            migrationBuilder.RenameTable(
                name: "ProductCategories",
                schema: "ContentDb",
                newName: "ProductCategories");

            migrationBuilder.RenameTable(
                name: "Marketplaces",
                schema: "ContentDb",
                newName: "Marketplaces");

            migrationBuilder.RenameTable(
                name: "CustomTemplates",
                schema: "ContentDb",
                newName: "CustomTemplates");
        }
    }
}
