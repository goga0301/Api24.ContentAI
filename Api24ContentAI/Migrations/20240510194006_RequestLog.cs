using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api24ContentAI.Migrations
{
    public partial class RequestLog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomTemplates_Marketplaces_MarketpalceId",
                schema: "ContentDb",
                table: "CustomTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Templates_ProductCategoryId",
                schema: "ContentDb",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_CustomTemplates_MarketpalceId",
                schema: "ContentDb",
                table: "CustomTemplates");

            migrationBuilder.RenameColumn(
                name: "MarketpalceId",
                schema: "ContentDb",
                table: "CustomTemplates",
                newName: "MarketplaceId");

            migrationBuilder.CreateTable(
                name: "RequestLogs",
                schema: "ContentDb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: true),
                    CreateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestLogs_Marketplaces_MarketplaceId",
                        column: x => x.MarketplaceId,
                        principalSchema: "ContentDb",
                        principalTable: "Marketplaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_ProductCategoryId",
                schema: "ContentDb",
                table: "Templates",
                column: "ProductCategoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomTemplates_MarketplaceId_ProductCategoryId",
                schema: "ContentDb",
                table: "CustomTemplates",
                columns: new[] { "MarketplaceId", "ProductCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_MarketplaceId",
                schema: "ContentDb",
                table: "RequestLogs",
                column: "MarketplaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomTemplates_Marketplaces_MarketplaceId",
                schema: "ContentDb",
                table: "CustomTemplates",
                column: "MarketplaceId",
                principalSchema: "ContentDb",
                principalTable: "Marketplaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomTemplates_Marketplaces_MarketplaceId",
                schema: "ContentDb",
                table: "CustomTemplates");

            migrationBuilder.DropTable(
                name: "RequestLogs",
                schema: "ContentDb");

            migrationBuilder.DropIndex(
                name: "IX_Templates_ProductCategoryId",
                schema: "ContentDb",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_CustomTemplates_MarketplaceId_ProductCategoryId",
                schema: "ContentDb",
                table: "CustomTemplates");

            migrationBuilder.RenameColumn(
                name: "MarketplaceId",
                schema: "ContentDb",
                table: "CustomTemplates",
                newName: "MarketpalceId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_ProductCategoryId",
                schema: "ContentDb",
                table: "Templates",
                column: "ProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomTemplates_MarketpalceId",
                schema: "ContentDb",
                table: "CustomTemplates",
                column: "MarketpalceId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomTemplates_Marketplaces_MarketpalceId",
                schema: "ContentDb",
                table: "CustomTemplates",
                column: "MarketpalceId",
                principalSchema: "ContentDb",
                principalTable: "Marketplaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
