using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api24ContentAI.Migrations
{
    /// <inheritdoc />
    public partial class DocumentTranslationChatHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTranslationChats",
                schema: "ContentDb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChatId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OriginalContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OriginalFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TargetLanguageId = table.Column<int>(type: "integer", nullable: false),
                    TargetLanguageName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTranslationChats", x => x.Id);
                    table.UniqueConstraint("AK_DocumentTranslationChats_ChatId", x => x.ChatId);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTranslationChatMessages",
                schema: "ContentDb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChatId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    TranslationJobId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AIModel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProcessingCost = table.Column<decimal>(type: "numeric", nullable: true),
                    ProcessingTimeSeconds = table.Column<int>(type: "integer", nullable: true),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTranslationChatMessages",
                schema: "ContentDb");

            migrationBuilder.DropTable(
                name: "DocumentTranslationChats",
                schema: "ContentDb");
        }
    }
}
