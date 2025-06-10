using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class AddSuggestionsToTranslationJob : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalContentType",
                schema: "ContentDb",
                table: "TranslationJobs");

            migrationBuilder.DropColumn(
                name: "OriginalFileData",
                schema: "ContentDb",
                table: "TranslationJobs");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                schema: "ContentDb",
                table: "TranslationJobs");

            migrationBuilder.AddColumn<string>(
                name: "Suggestions",
                schema: "ContentDb",
                table: "TranslationJobs",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Suggestions",
                schema: "ContentDb",
                table: "TranslationJobs");

            migrationBuilder.AddColumn<string>(
                name: "OriginalContentType",
                schema: "ContentDb",
                table: "TranslationJobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "OriginalFileData",
                schema: "ContentDb",
                table: "TranslationJobs",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                schema: "ContentDb",
                table: "TranslationJobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}
