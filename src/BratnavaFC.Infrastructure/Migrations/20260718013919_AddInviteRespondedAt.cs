using System;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Reconstruída a partir do schema de produção — arquivo original perdido, Id preservado
    /// para produção pular. A propriedade correspondente vive em outra branch.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260718013919_AddInviteRespondedAt")]
    public partial class AddInviteRespondedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InviteRespondedAt",
                table: "MatchPlayers",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InviteRespondedAt",
                table: "MatchPlayers");
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
