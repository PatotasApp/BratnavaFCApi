using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Remove MarkedByUserId de MonthlyPayments e ExtraChargePayments — colunas mortas.
    ///
    /// Elas vieram de AddPaymentMarkedByUser (20260718004338), uma migration reconstruída a
    /// partir do schema de produção porque o arquivo original se perdeu. As propriedades
    /// correspondentes viviam em feat/account-exit-financial-flow e feat/date-account-payment-flow,
    /// branches paradas desde julho/2026 e confirmadas como abandonadas — a versão final do
    /// fluxo financeiro é a que está em develop e main, e ela usa MarkedByAdminId.
    ///
    /// O resultado é que o banco carregava DUAS colunas para a mesma ideia, e a errada era a
    /// única com chave estrangeira. Quem lesse só a estrutura do banco concluiria que apagar
    /// um usuário anula o "marcado por" automaticamente — e não anula, porque a coluna viva
    /// (MarkedByAdminId) não tem FK nenhuma. Essa leitura equivocada é o motivo real deste
    /// drop: enquanto as duas existirem, a próxima pessoa cai na mesma armadilha.
    ///
    /// Nenhum código em develop ou main referencia MarkedByUserId; a única menção estava em
    /// migrations. Em desenvolvimento as duas colunas estavam com zero valores preenchidos.
    ///
    /// ATENÇÃO NO DEPLOY: confirme que produção também não tem dado nessas colunas antes de
    /// aplicar. O Down recria a estrutura, mas não devolve conteúdo.
    /// </summary>
    public partial class DropPaymentMarkedByUserColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyPayments_Users_MarkedByUserId",
                table: "MonthlyPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_ExtraChargePayments_Users_MarkedByUserId",
                table: "ExtraChargePayments");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyPayments_MarkedByUserId",
                table: "MonthlyPayments");

            migrationBuilder.DropIndex(
                name: "IX_ExtraChargePayments_MarkedByUserId",
                table: "ExtraChargePayments");

            migrationBuilder.DropColumn(
                name: "MarkedByUserId",
                table: "MonthlyPayments");

            migrationBuilder.DropColumn(
                name: "MarkedByUserId",
                table: "ExtraChargePayments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MarkedByUserId",
                table: "MonthlyPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MarkedByUserId",
                table: "ExtraChargePayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPayments_MarkedByUserId",
                table: "MonthlyPayments",
                column: "MarkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtraChargePayments_MarkedByUserId",
                table: "ExtraChargePayments",
                column: "MarkedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_MonthlyPayments_Users_MarkedByUserId",
                table: "MonthlyPayments",
                column: "MarkedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ExtraChargePayments_Users_MarkedByUserId",
                table: "ExtraChargePayments",
                column: "MarkedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
