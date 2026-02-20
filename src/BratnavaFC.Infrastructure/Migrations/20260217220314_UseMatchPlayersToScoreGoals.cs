using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseMatchPlayersToScoreGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Goals_Players_AssistPlayerId",
                table: "Goals");

            migrationBuilder.DropForeignKey(
                name: "FK_Goals_Players_ScorerPlayerId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_MatchId_AssistPlayerId",
                table: "Goals");

            migrationBuilder.RenameColumn(
                name: "ScorerPlayerId",
                table: "Goals",
                newName: "ScorerMatchPlayerId");

            migrationBuilder.RenameColumn(
                name: "AssistPlayerId",
                table: "Goals",
                newName: "MatchPlayerEntityId1");

            migrationBuilder.RenameIndex(
                name: "IX_Goals_ScorerPlayerId",
                table: "Goals",
                newName: "IX_Goals_ScorerMatchPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_Goals_MatchId_ScorerPlayerId",
                table: "Goals",
                newName: "IX_Goals_MatchId_ScorerMatchPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_Goals_AssistPlayerId",
                table: "Goals",
                newName: "IX_Goals_MatchPlayerEntityId1");

            migrationBuilder.AddColumn<Guid>(
                name: "AssistMatchPlayerId",
                table: "Goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MatchPlayerEntityId",
                table: "Goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Goals_AssistMatchPlayerId",
                table: "Goals",
                column: "AssistMatchPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchId_AssistMatchPlayerId",
                table: "Goals",
                columns: new[] { "MatchId", "AssistMatchPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchPlayerEntityId",
                table: "Goals",
                column: "MatchPlayerEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_MatchPlayers_AssistMatchPlayerId",
                table: "Goals",
                column: "AssistMatchPlayerId",
                principalTable: "MatchPlayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId",
                table: "Goals",
                column: "MatchPlayerEntityId",
                principalTable: "MatchPlayers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId1",
                table: "Goals",
                column: "MatchPlayerEntityId1",
                principalTable: "MatchPlayers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_MatchPlayers_ScorerMatchPlayerId",
                table: "Goals",
                column: "ScorerMatchPlayerId",
                principalTable: "MatchPlayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Goals_MatchPlayers_AssistMatchPlayerId",
                table: "Goals");

            migrationBuilder.DropForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId",
                table: "Goals");

            migrationBuilder.DropForeignKey(
                name: "FK_Goals_MatchPlayers_MatchPlayerEntityId1",
                table: "Goals");

            migrationBuilder.DropForeignKey(
                name: "FK_Goals_MatchPlayers_ScorerMatchPlayerId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_AssistMatchPlayerId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_MatchId_AssistMatchPlayerId",
                table: "Goals");

            migrationBuilder.DropIndex(
                name: "IX_Goals_MatchPlayerEntityId",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "AssistMatchPlayerId",
                table: "Goals");

            migrationBuilder.DropColumn(
                name: "MatchPlayerEntityId",
                table: "Goals");

            migrationBuilder.RenameColumn(
                name: "ScorerMatchPlayerId",
                table: "Goals",
                newName: "ScorerPlayerId");

            migrationBuilder.RenameColumn(
                name: "MatchPlayerEntityId1",
                table: "Goals",
                newName: "AssistPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_Goals_ScorerMatchPlayerId",
                table: "Goals",
                newName: "IX_Goals_ScorerPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_Goals_MatchPlayerEntityId1",
                table: "Goals",
                newName: "IX_Goals_AssistPlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_Goals_MatchId_ScorerMatchPlayerId",
                table: "Goals",
                newName: "IX_Goals_MatchId_ScorerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_MatchId_AssistPlayerId",
                table: "Goals",
                columns: new[] { "MatchId", "AssistPlayerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_Players_AssistPlayerId",
                table: "Goals",
                column: "AssistPlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Goals_Players_ScorerPlayerId",
                table: "Goals",
                column: "ScorerPlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
