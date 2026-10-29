using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionshipGameEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionshipGameEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ElapsedSeconds = table.Column<int>(type: "integer", nullable: true),
                    QueueId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionshipGameEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChampionshipGameEvents_Championships_ChampionshipId",
                        column: x => x.ChampionshipId,
                        principalTable: "Championships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChampionshipGameEvents_Matches_GameId",
                        column: x => x.GameId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipGameEvents_ChampionshipId_GameId_EventTimeUtc",
                table: "ChampionshipGameEvents",
                columns: new[] { "ChampionshipId", "GameId", "EventTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionshipGameEvents_GameId",
                table: "ChampionshipGameEvents",
                column: "GameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionshipGameEvents");
        }
    }
}
