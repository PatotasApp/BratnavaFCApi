using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Permite que uma patota exista sem dono.
    ///
    /// CreatedByUserId é POSSE, não autoria — o TransferCreator reaponta o campo quando
    /// alguém passa a patota adiante. Enquanto ele era NOT NULL, o criador ficava preso:
    /// excluir a conta dele deixaria a coluna apontando para um usuário inexistente, e o
    /// fluxo leave-creator (que exige CreatedByUserId == requestingUserId) nunca mais
    /// rodaria naquela patota — posse perdida para sempre, sem recuperação pelo produto.
    ///
    /// Com a coluna anulável, quem criou a patota pode excluir a conta sem antes transferir:
    /// a patota continua sendo administrada por quem já é admin, apenas sem dono. Quem
    /// exibir esse campo mostra "Usuário deletado" ao encontrar nulo.
    ///
    /// O bloqueio da exclusão de conta permanece para o caso que realmente quebra o uso:
    /// ser o ÚNICO admin de uma patota, o que a deixaria sem ninguém podendo administrá-la.
    /// </summary>
    public partial class MakeGroupCreatorNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Groups",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Groups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
