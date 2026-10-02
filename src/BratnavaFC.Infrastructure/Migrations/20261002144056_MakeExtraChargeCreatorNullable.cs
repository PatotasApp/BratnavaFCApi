using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Torna ExtraCharges.CreatedByAdminId anulável, para que a exclusão de conta possa
    /// zerá-la como já faz com Groups, Polls, CalendarEvents, GroupTransactions e os dois
    /// tipos de pagamento.
    ///
    /// A coluna era NOT NULL e não tem chave estrangeira. O resultado é que, ao excluir a
    /// conta, a cobrança ficava apontando para um usuário inexistente — foi o que uma
    /// varredura de todas as colunas de usuário contra o Postgres flagrou, depois de um
    /// teste ponta a ponta. A suíte não pegaria: o provider InMemory não tem schema nem FK.
    ///
    /// Hoje ninguém lê esse campo (nem API, nem front, nem app), então não havia tela
    /// errada — havia o mesmo convite ao engano que motivou DropPaymentMarkedByUserColumns:
    /// quem ler o banco assume que o id aponta para alguém.
    ///
    /// O Down converte os nulos para Guid.Empty, porque a coluna volta a ser NOT NULL.
    /// </summary>
    public partial class MakeExtraChargeCreatorNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByAdminId",
                table: "ExtraCharges",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByAdminId",
                table: "ExtraCharges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
