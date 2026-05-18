using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPushTokenUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove índice único apenas em Token (permitia roubo de token entre usuários)
            migrationBuilder.DropIndex(
                name: "IX_PushTokens_Token",
                table: "PushTokens");

            // Remove índice simples em UserId (será substituído pelo composto)
            migrationBuilder.DropIndex(
                name: "IX_PushTokens_UserId",
                table: "PushTokens");

            // Índice único em (UserId, Token): mesmo dispositivo pode ter linhas
            // para usuários diferentes sem roubar o token de ninguém.
            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_UserId_Token",
                table: "PushTokens",
                columns: new[] { "UserId", "Token" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PushTokens_UserId_Token",
                table: "PushTokens");

            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_UserId",
                table: "PushTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PushTokens_Token",
                table: "PushTokens",
                column: "Token",
                unique: true);
        }
    }
}
