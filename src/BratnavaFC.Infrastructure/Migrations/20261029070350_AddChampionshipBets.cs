using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionshipBets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionshipBets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionshipBets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChampionshipBets_Championships_ChampionshipId",
                        column: x => x.ChampionshipId,
                        principalTable: "Championships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChampionshipBetSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<short>(type: "smallint", nullable: false),
                    PredictedId = table.Column<Guid>(type: "uuid", nullable: false),
                    FichasWagered = table.Column<int>(type: "integer", nullable: false),
                    ActualIds = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    FichasEarned = table.Column<int>(type: "integer", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionshipBetSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChampionshipBetSelections_ChampionshipBets_BetId",
                        column: x => x.BetId,
                        principalTable: "ChampionshipBets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipBets_ChampionshipId_UserId",
                table: "ChampionshipBets",
                columns: new[] { "ChampionshipId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipBets_GroupId_ChampionshipId",
                table: "ChampionshipBets",
                columns: new[] { "GroupId", "ChampionshipId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipBetSelections_BetId_Category",
                table: "ChampionshipBetSelections",
                columns: new[] { "BetId", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionshipBetSelections");

            migrationBuilder.DropTable(
                name: "ChampionshipBets");
        }
    }
}
