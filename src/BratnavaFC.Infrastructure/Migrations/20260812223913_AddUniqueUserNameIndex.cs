using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueUserNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sem backfill de propósito: diferente do e-mail, o UserName não é normalizado na
            // escrita, e reescrever handle de usuário seria mudança visível que ninguém pediu.
            //
            // Se houver handle duplicado, a criação abaixo falha e aborta a migration. É o
            // comportamento desejado — o dado precisa ser resolvido à mão antes, e um deploy
            // que quebra aqui é melhor que um identificador ambíguo já em uso.
            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");
        }
    }
}
