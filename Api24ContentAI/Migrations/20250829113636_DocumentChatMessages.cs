using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api24ContentAI.Migrations
{
    /// <inheritdoc />
    public partial class DocumentChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTranslationChatMessages",
                schema: "ContentDb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChatId = table.Column<Guid>(type: "uuid", maxLength: 50, nullable: false),
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
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTranslationChatMessages_ChatId",
                schema: "ContentDb",
                table: "DocumentTranslationChatMessages",
                column: "ChatId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTranslationChatMessages",
                schema: "ContentDb");
        }
    }
}
