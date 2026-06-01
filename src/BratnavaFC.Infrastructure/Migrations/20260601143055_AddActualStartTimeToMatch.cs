using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualStartTimeToMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualStartTime",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualStartTime",
                table: "Matches");
        }
    }
}
