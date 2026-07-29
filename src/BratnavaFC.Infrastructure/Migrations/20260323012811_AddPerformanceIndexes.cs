using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Reconstruída a partir do schema de produção: o arquivo original foi perdido e não
    /// existe em nenhuma branch, mas o Id já consta no __EFMigrationsHistory de produção —
    /// por isso o Id é preservado e produção pula esta migration.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260323012811_AddPerformanceIndexes")]
    public partial class AddPerformanceIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Players_GroupId_UserId",
                table: "Players",
                columns: new[] { "GroupId", "UserId" });

            // Produção tem esta extension instalada. Nada no código a usa hoje, mas ela é
            // mantida aqui para banco novo reproduzir o mesmo estado.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_GroupId_UserId",
                table: "Players");
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
