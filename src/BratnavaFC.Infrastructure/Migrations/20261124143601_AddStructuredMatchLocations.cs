using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BratnavaFC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredMatchLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaceAddress",
                table: "Matches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PlaceLatitude",
                table: "Matches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PlaceLongitude",
                table: "Matches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPlaceAddress",
                table: "GroupSettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPlaceLatitude",
                table: "GroupSettings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPlaceLongitude",
                table: "GroupSettings",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlaceAddress",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlaceLatitude",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlaceLongitude",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "DefaultPlaceAddress",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPlaceLatitude",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPlaceLongitude",
                table: "GroupSettings");

        }
    }
}
