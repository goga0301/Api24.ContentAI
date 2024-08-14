using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class addedEmailAuthorizationCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AuthorizationCode",
                schema: "ContentDb",
                table: "Users",
                newName: "EmailAuthorizationCode");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EmailAuthorizationCode",
                schema: "ContentDb",
                table: "Users",
                newName: "AuthorizationCode");
        }
    }
}
