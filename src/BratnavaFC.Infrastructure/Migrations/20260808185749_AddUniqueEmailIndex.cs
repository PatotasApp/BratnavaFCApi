using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueEmailIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UserEntity.SetEmail normaliza para minúsculas, mas isso é recente: linhas
            // gravadas antes podem ter caixa mista, e o índice único trataria "A@x.com" e
            // "a@x.com" como valores distintos — deixando passar exatamente o duplicado que
            // ele existe para impedir. Normalizar antes torna o índice simples suficiente.
            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "Email" = lower(trim("Email"))
                WHERE "Email" <> lower(trim("Email"));
                """);

            // Se houver duplicado real (mesmo e-mail em duas linhas), a criação abaixo falha e
            // aborta a migration. É o comportamento desejado: o dado precisa ser resolvido à
            // mão antes, e um deploy que quebra aqui é melhor que uma identidade ambígua.
            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");
        }
    }
}
