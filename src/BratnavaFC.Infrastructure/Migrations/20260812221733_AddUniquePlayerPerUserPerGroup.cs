using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniquePlayerPerUserPerGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop escrito à mão: a migration AddPerformanceIndexes (20260323012811) criou este
            // índice como NÃO-único e nunca o declarou no modelo, então o EF não sabe que ele
            // existe e não gera o drop sozinho. Sem remover primeiro, o CreateIndex abaixo
            // falha com 42P07 (relation already exists) — foi exatamente o que aconteceu na
            // primeira tentativa de aplicar esta migration.
            migrationBuilder.DropIndex(
                name: "IX_Players_GroupId_UserId",
                table: "Players");

            migrationBuilder.CreateIndex(
                name: "IX_Players_GroupId_UserId",
                table: "Players",
                columns: new[] { "GroupId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_GroupId_UserId",
                table: "Players");

            // Devolve o índice não-único que o AddPerformanceIndexes havia criado, para o Down
            // reproduzir o estado anterior de verdade.
            migrationBuilder.CreateIndex(
                name: "IX_Players_GroupId_UserId",
                table: "Players",
                columns: new[] { "GroupId", "UserId" });
        }
    }
}
