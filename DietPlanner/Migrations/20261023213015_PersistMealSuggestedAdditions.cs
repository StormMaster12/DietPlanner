using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietPlanner.Migrations
{
    /// <inheritdoc />
    public partial class PersistMealSuggestedAdditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealAdditionSuggestionCache");

            migrationBuilder.CreateTable(
                name: "MealAdditionSuggestionSets",
                columns: table => new
                {
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MealFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealAdditionSuggestionSets", x => x.MealId);
                    table.ForeignKey(
                        name: "FK_MealAdditionSuggestionSets_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealSuggestedAdditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealSuggestedAdditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealSuggestedAdditions_MealAdditionSuggestionSets_MealId",
                        column: x => x.MealId,
                        principalTable: "MealAdditionSuggestionSets",
                        principalColumn: "MealId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealSuggestedAdditions_MealId",
                table: "MealSuggestedAdditions",
                column: "MealId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealSuggestedAdditions");

            migrationBuilder.DropTable(
                name: "MealAdditionSuggestionSets");

            migrationBuilder.CreateTable(
                name: "MealAdditionSuggestionCache",
                columns: table => new
                {
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestionsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealAdditionSuggestionCache", x => x.MealId);
                });
        }
    }
}
