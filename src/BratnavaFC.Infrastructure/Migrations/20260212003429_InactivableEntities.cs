using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InactivableEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "Users",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "InactivatedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "Players",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "InactivatedAt",
                table: "Players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "Groups",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "InactivatedAt",
                table: "Groups",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InactivatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InactivatedAt",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "InactivatedAt",
                table: "Groups");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldDefaultValue: (short)1);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldDefaultValue: (short)1);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldDefaultValue: (short)1);
        }
    }
}
