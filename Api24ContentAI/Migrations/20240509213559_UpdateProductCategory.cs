using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class UpdateProductCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Api24Id",
                schema: "ContentDb",
                table: "ProductCategories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameEng",
                schema: "ContentDb",
                table: "ProductCategories",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Api24Id",
                schema: "ContentDb",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "NameEng",
                schema: "ContentDb",
                table: "ProductCategories");
        }
    }
}
