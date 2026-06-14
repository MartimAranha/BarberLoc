using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBarberShopPlacesCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BarberShopPlaces");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BarberShopPlaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastFetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OpeningHoursJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PhotoReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PlaceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rating = table.Column<double>(type: "float", nullable: true),
                    UserRatingsTotal = table.Column<int>(type: "int", nullable: true),
                    Website = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarberShopPlaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BarberShopPlaces_Category_Rating",
                table: "BarberShopPlaces",
                columns: new[] { "Category", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_BarberShopPlaces_PlaceId",
                table: "BarberShopPlaces",
                column: "PlaceId",
                unique: true);
        }
    }
}
