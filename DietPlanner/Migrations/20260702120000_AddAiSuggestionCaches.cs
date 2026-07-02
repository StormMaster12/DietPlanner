using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietPlanner.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSuggestionCaches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MealAdditionSuggestionCache",
                columns: table => new
                {
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealAdditionSuggestionCache", x => x.MealId);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingListGroupingCache",
                columns: table => new
                {
                    WeekStartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    GroupedJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingListGroupingCache", x => x.WeekStartDate);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealAdditionSuggestionCache");

            migrationBuilder.DropTable(
                name: "ShoppingListGroupingCache");
        }
    }
}
