using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoModeAndLocalSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "BarberShopPlaces",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BarberShopPlaces_Category_Rating",
                table: "BarberShopPlaces",
                columns: new[] { "Category", "Rating" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BarberShopPlaces_Category_Rating",
                table: "BarberShopPlaces");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "BarberShopPlaces");
        }
    }
}
