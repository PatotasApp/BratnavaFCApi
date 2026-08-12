using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MaterializeAchievementsAndProfilePrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "ProfileVisibility",
                table: "Users",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPatotaNamesOnProfile",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowZoeiraAchievementsOnProfile",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlayerMatchStatContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    PlayedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    IsGoalkeeper = table.Column<bool>(type: "boolean", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Goals = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    Mvps = table.Column<int>(type: "integer", nullable: false),
                    OwnGoals = table.Column<int>(type: "integer", nullable: false),
                    CleanSheets = table.Column<int>(type: "integer", nullable: false),
                    HatTricks = table.Column<int>(type: "integer", nullable: false),
                    Pokers = table.Column<int>(type: "integer", nullable: false),
                    FiveGoalGames = table.Column<int>(type: "integer", nullable: false),
                    GoalAndAssistGames = table.Column<int>(type: "integer", nullable: false),
                    ThreeAssistGames = table.Column<int>(type: "integer", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMatchStatContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatContributions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerMatchStatContributions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerStatProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Goals = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    Mvps = table.Column<int>(type: "integer", nullable: false),
                    OwnGoals = table.Column<int>(type: "integer", nullable: false),
                    CleanSheets = table.Column<int>(type: "integer", nullable: false),
                    HatTricks = table.Column<int>(type: "integer", nullable: false),
                    Pokers = table.Column<int>(type: "integer", nullable: false),
                    FiveGoalGames = table.Column<int>(type: "integer", nullable: false),
                    GoalAndAssistGames = table.Column<int>(type: "integer", nullable: false),
                    ThreeAssistGames = table.Column<int>(type: "integer", nullable: false),
                    UnbeatenFiveRuns = table.Column<int>(type: "integer", nullable: false),
                    UnbeatenTenRuns = table.Column<int>(type: "integer", nullable: false),
                    FiveWinRuns = table.Column<int>(type: "integer", nullable: false),
                    ThreeCleanSheetRuns = table.Column<int>(type: "integer", nullable: false),
                    ProjectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStatProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerStatProjections_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonTitles_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatContributions_GroupId_PlayerId_Season",
                table: "PlayerMatchStatContributions",
                columns: new[] { "GroupId", "PlayerId", "Season" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatContributions_MatchId_PlayerId",
                table: "PlayerMatchStatContributions",
                columns: new[] { "MatchId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStatContributions_PlayerId",
                table: "PlayerMatchStatContributions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerStatProjections_GroupId_PlayerId_Season",
                table: "PlayerStatProjections",
                columns: new[] { "GroupId", "PlayerId", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerStatProjections_PlayerId",
                table: "PlayerStatProjections",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonTitles_GroupId_Season_Category_PlayerId",
                table: "SeasonTitles",
                columns: new[] { "GroupId", "Season", "Category", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonTitles_PlayerId",
                table: "SeasonTitles",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerMatchStatContributions");

            migrationBuilder.DropTable(
                name: "PlayerStatProjections");

            migrationBuilder.DropTable(
                name: "SeasonTitles");

            migrationBuilder.DropColumn(
                name: "ProfileVisibility",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShowPatotaNamesOnProfile",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShowZoeiraAchievementsOnProfile",
                table: "Users");
        }
    }
}
