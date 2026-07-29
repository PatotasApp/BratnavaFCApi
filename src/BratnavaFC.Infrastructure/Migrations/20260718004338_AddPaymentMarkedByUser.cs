using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMarkedByUser : Migration
    {
        /// <inheritdoc />
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
                name: "FK_ExtraChargePayments_Users_MarkedByUserId",
                table: "ExtraChargePayments",
                column: "MarkedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MonthlyPayments_Users_MarkedByUserId",
                table: "MonthlyPayments",
                column: "MarkedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExtraChargePayments_Users_MarkedByUserId",
                table: "ExtraChargePayments");

            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyPayments_Users_MarkedByUserId",
                table: "MonthlyPayments");

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
    }
}
