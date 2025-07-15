using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api24ContentAI.Migrations
{
    /// <inheritdoc />
    public partial class AddAIModelToTranslationJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AIModel",
                schema: "ContentDb",
                table: "TranslationJobs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AIModel",
                schema: "ContentDb",
                table: "TranslationJobs");
        }
    }
}
