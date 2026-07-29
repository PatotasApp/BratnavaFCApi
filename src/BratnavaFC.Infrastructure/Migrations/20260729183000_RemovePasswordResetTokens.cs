using System;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Remove PasswordResetTokens: a recuperação de senha passa a ser responsabilidade do
    /// Firebase Auth, então a tabela e os códigos guardados nela deixam de ter uso.
    ///
    /// Diferente das migrations reconstruídas, esta tem Id novo — é a única que executa em
    /// produção. O Down() recria a estrutura por completo, mas não os dados.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260729183000_RemovePasswordResetTokens")]
    public partial class RemovePasswordResetTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DROP TABLE remove junto o PK, o índice e a FK de saída para Users.
            migrationBuilder.DropTable(name: "PasswordResetTokens");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_IsUsed_ExpiresAt",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "IsUsed", "ExpiresAt" });
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
