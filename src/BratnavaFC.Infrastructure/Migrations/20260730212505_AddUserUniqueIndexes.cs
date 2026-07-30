using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Torna UserName e Email únicos no banco.
    ///
    /// Até aqui a unicidade era só aplicacional, e o email era comparado sem normalizar —
    /// "Joao@x.com" e "joao@x.com" passavam como usuários distintos, porque o Postgres compara
    /// string case-sensitive. Isso deixa de ser aceitável quando o email vira credencial no
    /// Firebase Auth, que exige endereço único e compara sem diferenciar caixa.
    ///
    /// O UPDATE antes dos índices é defensivo: em produção os dados já estão em minúsculas,
    /// mas bases de desenvolvimento podem não estar, e a entidade só passou a normalizar na
    /// escrita agora. Se ainda assim existirem dois registros que colidam depois de
    /// normalizados, o CREATE UNIQUE INDEX falha e a transação inteira reverte — falhar aqui
    /// é melhor que descobrir a colisão na hora de criar a credencial no Firebase.
    /// </summary>
    public partial class AddUserUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "UserName" = lower(btrim("UserName")),
                    "Email"    = lower(btrim("Email"))
                WHERE "UserName" <> lower(btrim("UserName"))
                   OR "Email"    <> lower(btrim("Email"));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A normalização não é revertida: não guardamos a caixa original, e reverter o
            // índice não deveria reintroduzir dados ambíguos.
            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");
        }
    }
}
