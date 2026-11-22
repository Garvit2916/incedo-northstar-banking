using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Northstar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArticleCode = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TagsCsv = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ApplicableProduct = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IssueType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    KeywordsCsv = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ResolutionStepsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CustomerExplanation = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    AgentGuidance = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    EscalationCriteria = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_ArticleCode",
                table: "KnowledgeArticles",
                column: "ArticleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_IsActive_Category",
                table: "KnowledgeArticles",
                columns: new[] { "IsActive", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeArticles");
        }
    }
}
