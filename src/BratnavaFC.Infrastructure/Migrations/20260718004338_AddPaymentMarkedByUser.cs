using System;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <summary>
    /// Reconstruída a partir do schema de produção — arquivo original perdido, Id preservado
    /// para produção pular. As propriedades correspondentes vivem em outra branch.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260718004338_AddPaymentMarkedByUser")]
    public partial class AddPaymentMarkedByUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExtraChargePayments_Users_MarkedByUserId",
                table: "ExtraChargePayments");

            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyPayments_Users_MarkedByUserId",
                table: "MonthlyPayments");

            migrationBuilder.DropIndex(
                name: "IX_ExtraChargePayments_MarkedByUserId",
                table: "ExtraChargePayments");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyPayments_MarkedByUserId",
                table: "MonthlyPayments");

            migrationBuilder.DropColumn(name: "MarkedByUserId", table: "ExtraChargePayments");
            migrationBuilder.DropColumn(name: "MarkedByUserId", table: "MonthlyPayments");
        }

        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
        }
    }
}
