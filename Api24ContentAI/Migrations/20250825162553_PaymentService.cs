using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api24ContentAI.Migrations
{
    /// <inheritdoc />
    public partial class PaymentService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTranslationChats_ChatId",
                schema: "ContentDb",
                table: "DocumentTranslationChats");

            migrationBuilder.DropIndex(
                name: "IX_DocumentTranslationChats_UserId",
                schema: "ContentDb",
                table: "DocumentTranslationChats");

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "ContentDb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<int>(type: "integer", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ContentDb",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId",
                schema: "ContentDb",
                table: "Payments",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments",
                schema: "ContentDb");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTranslationChats_ChatId",
                schema: "ContentDb",
                table: "DocumentTranslationChats",
                column: "ChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTranslationChats_UserId",
                schema: "ContentDb",
                table: "DocumentTranslationChats",
                column: "UserId");
        }
    }
}
