using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api24ContentAI.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyDocumentTranslationChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTranslationChatMessages",
                schema: "ContentDb");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DocumentTranslationChats_ChatId",
                schema: "ContentDb",
                table: "DocumentTranslationChats");

            migrationBuilder.DropColumn(
                name: "MessageCount",
                schema: "ContentDb",
                table: "DocumentTranslationChats");

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                schema: "ContentDb",
                table: "DocumentTranslationChats",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranslationResult",
                schema: "ContentDb",
                table: "DocumentTranslationChats",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                schema: "ContentDb",
                table: "DocumentTranslationChats");

            migrationBuilder.DropColumn(
                name: "TranslationResult",
                schema: "ContentDb",
                table: "DocumentTranslationChats");

            migrationBuilder.AddColumn<int>(
                name: "MessageCount",
                schema: "ContentDb",
                table: "DocumentTranslationChats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DocumentTranslationChats_ChatId",
                schema: "ContentDb",
                table: "DocumentTranslationChats",
                column: "ChatId");

            migrationBuilder.CreateTable(
                name: "DocumentTranslationChatMessages",
                schema: "ContentDb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AIModel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    ProcessingCost = table.Column<decimal>(type: "numeric", nullable: true),
                    ProcessingTimeSeconds = table.Column<int>(type: "integer", nullable: true),
                    TranslationJobId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTranslationChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTranslationChatMessages_DocumentTranslationChats_Ch~",
                        column: x => x.ChatId,
                        principalSchema: "ContentDb",
                        principalTable: "DocumentTranslationChats",
                        principalColumn: "ChatId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTranslationChatMessages_ChatId_CreatedAt",
                schema: "ContentDb",
                table: "DocumentTranslationChatMessages",
                columns: new[] { "ChatId", "CreatedAt" });
        }
    }
}
