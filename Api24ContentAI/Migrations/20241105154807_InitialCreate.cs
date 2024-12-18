using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseJson",
                schema: "ContentDb",
                table: "RequestLogs",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseJson",
                schema: "ContentDb",
                table: "RequestLogs");
        }
    }
}
