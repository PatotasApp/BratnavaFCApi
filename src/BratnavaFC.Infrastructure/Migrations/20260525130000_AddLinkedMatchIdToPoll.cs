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
            // No-op: Polls.LinkedMatchId, FK, and index were already created
            // by migration 20260525120000_AddLinkedPollToMatch.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: nothing was added in Up().
        }
    }
}
