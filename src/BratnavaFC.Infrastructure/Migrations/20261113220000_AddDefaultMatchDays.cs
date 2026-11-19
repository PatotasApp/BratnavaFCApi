using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113220000_AddDefaultMatchDays")]
public partial class AddDefaultMatchDays : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DefaultDaysOfWeekJson",
            table: "GroupSettings",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DefaultDaysOfWeekJson",
            table: "GroupSettings");
    }
}
