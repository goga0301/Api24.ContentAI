using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class AddLanguageInTemplates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Templates_ProductCategoryId",
                schema: "ContentDb",
                table: "Templates");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "ContentDb",
                table: "Templates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "ContentDb",
                table: "CustomTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Templates_ProductCategoryId_Language",
                schema: "ContentDb",
                table: "Templates",
                columns: new[] { "ProductCategoryId", "Language" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Templates_ProductCategoryId_Language",
                schema: "ContentDb",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Language",
                schema: "ContentDb",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Language",
                schema: "ContentDb",
                table: "CustomTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_ProductCategoryId",
                schema: "ContentDb",
                table: "Templates",
                column: "ProductCategoryId",
                unique: true);
        }
    }
}
