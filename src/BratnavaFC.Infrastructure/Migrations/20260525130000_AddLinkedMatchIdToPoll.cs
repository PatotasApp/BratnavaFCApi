using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Adiciona a FK reversa Polls.LinkedMatchId → Matches.Id.
    /// A coluna Matches.LinkedPollId já foi criada pela migração anterior
    /// (20260525120000_AddLinkedPollToMatch); esta migração completa o vínculo bidirecional.
    /// </summary>
    public partial class AddLinkedMatchIdToPoll : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
