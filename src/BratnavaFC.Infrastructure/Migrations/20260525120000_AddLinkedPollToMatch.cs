using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Cria o lado Matches do vínculo com Votações/Eventos: Matches.LinkedPollId aponta
    /// para a votação vinculada à partida. O lado oposto (Polls.LinkedMatchId) é criado
    /// pela migration seguinte, 20260525130000_AddLinkedMatchIdToPoll.
    /// Coluna nullable; ON DELETE SET NULL garante que excluir um lado não quebra o outro.
    /// </summary>
    public partial class AddLinkedPollToMatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
