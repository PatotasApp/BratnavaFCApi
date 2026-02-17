using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalsToMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScorerPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TimeSeconds = table.Column<int>(type: "integer", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Goals_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Goals_Players_AssistPlayerId",
                        column: x => x.AssistPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Goals_Players_ScorerPlayerId",
                        column: x => x.ScorerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_AssistPlayerId",
                table: "Goals",
                column: "AssistPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_GroupId",
                table: "Goals",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchId_AssistPlayerId",
                table: "Goals",
                columns: new[] { "MatchId", "AssistPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchId_ScorerPlayerId",
                table: "Goals",
                columns: new[] { "MatchId", "ScorerPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_ScorerPlayerId",
                table: "Goals",
                column: "ScorerPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Goals");
        }
    }
}
