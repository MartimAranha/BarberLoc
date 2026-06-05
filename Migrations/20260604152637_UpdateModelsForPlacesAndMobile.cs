using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModelsForPlacesAndMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMobile",
                table: "Services",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TargetGender",
                table: "Services",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnSite",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "TravelDistanceKm",
                table: "Bookings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TravelFee",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceId",
                table: "Barbershops",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMobile",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "TargetGender",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "IsOnSite",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TravelDistanceKm",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TravelFee",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "Barbershops");
        }
    }
}
