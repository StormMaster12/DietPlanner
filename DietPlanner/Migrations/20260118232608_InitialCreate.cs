using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietPlanner.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Slots",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slots", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Meals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SlotKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Kcal = table.Column<int>(type: "INTEGER", nullable: false),
                    ProteinG = table.Column<int>(type: "INTEGER", nullable: false),
                    FibreG = table.Column<int>(type: "INTEGER", nullable: false),
                    Plants = table.Column<int>(type: "INTEGER", nullable: false),
                    ZoeNotes = table.Column<string>(type: "TEXT", nullable: true),
                    MfName = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meals_Slots_SlotKey",
                        column: x => x.SlotKey,
                        principalTable: "Slots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeekPlanEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SlotKey = table.Column<int>(type: "INTEGER", nullable: false),
                    MealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortionMultiplier = table.Column<decimal>(type: "TEXT", precision: 6, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekPlanEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeekPlanEntries_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeekPlanEntries_Slots_SlotKey",
                        column: x => x.SlotKey,
                        principalTable: "Slots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Meals_SlotKey",
                table: "Meals",
                column: "SlotKey");

            migrationBuilder.CreateIndex(
                name: "IX_WeekPlanEntries_Date_SlotKey",
                table: "WeekPlanEntries",
                columns: new[] { "Date", "SlotKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeekPlanEntries_MealId",
                table: "WeekPlanEntries",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_WeekPlanEntries_SlotKey",
                table: "WeekPlanEntries",
                column: "SlotKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "WeekPlanEntries");

            migrationBuilder.DropTable(
                name: "Meals");

            migrationBuilder.DropTable(
                name: "Slots");
        }
    }
}
