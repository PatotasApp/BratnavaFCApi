using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Cria vínculo bidirecional entre Partidas e Votações/Eventos:
    ///   - Matches.LinkedPollId  → qual votação está vinculada à partida
    ///   - Polls.LinkedMatchId   → em qual partida esta votação está vinculada
    /// Ambas as colunas são nullable; ON DELETE SET NULL garante que
    /// excluir um lado não quebra o outro.
    /// </summary>
    public partial class AddLinkedPollToMatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Matches.LinkedPollId ──────────────────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedPollId",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_LinkedPollId",
                table: "Matches",
                column: "LinkedPollId");

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Polls_LinkedPollId",
                table: "Matches",
                column: "LinkedPollId",
                principalTable: "Polls",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Polls.LinkedMatchId ───────────────────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedMatchId",
                table: "Polls",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Polls_LinkedMatchId",
                table: "Polls",
                column: "LinkedMatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Polls_Matches_LinkedMatchId",
                table: "Polls",
                column: "LinkedMatchId",
                principalTable: "Matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Polls_LinkedPollId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_LinkedPollId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "LinkedPollId",
                table: "Matches");

            migrationBuilder.DropForeignKey(
                name: "FK_Polls_Matches_LinkedMatchId",
                table: "Polls");

            migrationBuilder.DropIndex(
                name: "IX_Polls_LinkedMatchId",
                table: "Polls");

            migrationBuilder.DropColumn(
                name: "LinkedMatchId",
                table: "Polls");
        }
    }
}
