using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChampionshipExternalWinnerTeamId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChampionshipGameOrder",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChampionshipId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChampionshipTeamAId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChampionshipTeamBId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Championships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlaceName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Championships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Championships_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChampionshipPlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteResponse = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsGoalkeeper = table.Column<bool>(type: "boolean", nullable: false),
                    DidNotPlay = table.Column<bool>(type: "boolean", nullable: false),
                    IsMvp = table.Column<bool>(type: "boolean", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionshipPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChampionshipPlayers_Championships_ChampionshipId",
                        column: x => x.ChampionshipId,
                        principalTable: "Championships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChampionshipPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChampionshipTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ColorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    FinalPosition = table.Column<int>(type: "integer", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionshipTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChampionshipTeams_Championships_ChampionshipId",
                        column: x => x.ChampionshipId,
                        principalTable: "Championships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChampionshipTeams_TeamColors_ColorId",
                        column: x => x.ColorId,
                        principalTable: "TeamColors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChampionshipVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: true),
                    VoterPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VotedPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionshipVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChampionshipVotes_Championships_ChampionshipId",
                        column: x => x.ChampionshipId,
                        principalTable: "Championships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ChampionshipId",
                table: "Matches",
                column: "ChampionshipId");

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipPlayers_ChampionshipId_PlayerId",
                table: "ChampionshipPlayers",
                columns: new[] { "ChampionshipId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipPlayers_PlayerId",
                table: "ChampionshipPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Championships_GroupId_PlayedAt",
                table: "Championships",
                columns: new[] { "GroupId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipTeams_ChampionshipId",
                table: "ChampionshipTeams",
                column: "ChampionshipId");

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipTeams_ColorId",
                table: "ChampionshipTeams",
                column: "ColorId");

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipVotes_ChampionshipId_GameId_VoterPlayerId",
                table: "ChampionshipVotes",
                columns: new[] { "ChampionshipId", "GameId", "VoterPlayerId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Championships_ChampionshipId",
                table: "Matches",
                column: "ChampionshipId",
                principalTable: "Championships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Championships_ChampionshipId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "ChampionshipPlayers");

            migrationBuilder.DropTable(
                name: "ChampionshipTeams");

            migrationBuilder.DropTable(
                name: "ChampionshipVotes");

            migrationBuilder.DropTable(
                name: "Championships");

            migrationBuilder.DropIndex(
                name: "IX_Matches_ChampionshipId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ChampionshipExternalWinnerTeamId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ChampionshipGameOrder",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ChampionshipId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ChampionshipTeamAId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ChampionshipTeamBId",
                table: "Matches");
        }
    }
}
