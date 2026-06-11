using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddBarbershopGooglePlacesFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GooglePlaceId",
                table: "Barbershops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Rating",
                table: "Barbershops",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Barbershops",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Barbershops_GooglePlaceId",
                table: "Barbershops",
                column: "GooglePlaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Barbershops_GooglePlaceId",
                table: "Barbershops");

            migrationBuilder.DropColumn(
                name: "GooglePlaceId",
                table: "Barbershops");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Barbershops");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Barbershops");
        }
    }
}
