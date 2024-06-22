using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class VideoScriptAi : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VideoScriptLimit",
                schema: "ContentDb",
                table: "Marketplaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoScriptLimit",
                schema: "ContentDb",
                table: "Marketplaces");
        }
    }
}
