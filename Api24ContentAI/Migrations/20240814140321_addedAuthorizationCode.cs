using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class addedAuthorizationCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorizationCode",
                schema: "ContentDb",
                table: "Users",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorizationCode",
                schema: "ContentDb",
                table: "Users");
        }
    }
}
