using System;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260713120000_AddMatchSchedulingSettings")]
    public partial class AddMatchSchedulingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MatchSchedulingEnabled",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "MatchSchedulingMode",
                table: "GroupSettings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<int>(
                name: "MatchScheduleDayOfWeek",
                table: "GroupSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "MatchScheduleTime",
                table: "GroupSettings",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualMatchSchedulesJson",
                table: "GroupSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchSchedulingEnabled",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MatchSchedulingMode",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MatchScheduleDayOfWeek",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MatchScheduleTime",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ManualMatchSchedulesJson",
                table: "GroupSettings");
        }
    }
}
