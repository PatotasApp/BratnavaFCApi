using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupsToTeamCollors : Migration
    {
        private static readonly Guid DefaultGroupId = new Guid("3f401edf-d309-4bae-97d8-28eae0da7a8a");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "TeamColors",
                type: "uuid",
                nullable: false,
                defaultValue: DefaultGroupId);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TeamColors",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamColors_GroupId_Name",
                table: "TeamColors",
                columns: new[] { "GroupId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_TeamColors_Groups_GroupId",
                table: "TeamColors",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // OPCIONAL (recomendado):
            // se você NÃO quer que novos TeamColors caiam nesse GroupId "por padrão" quando esquecerem de enviar,
            // remova o default depois da migração:
            //
            // migrationBuilder.AlterColumn<Guid>(
            //     name: "GroupId",
            //     table: "TeamColors",
            //     type: "uuid",
            //     nullable: false,
            //     oldClrType: typeof(Guid),
            //     oldType: "uuid",
            //     oldDefaultValue: DefaultGroupId);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamColors_Groups_GroupId",
                table: "TeamColors");

            migrationBuilder.DropIndex(
                name: "IX_TeamColors_GroupId_Name",
                table: "TeamColors");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "TeamColors");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TeamColors");
        }
    }
}
