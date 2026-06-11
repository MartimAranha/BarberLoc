using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalStatusToBarbershop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerifiedAt",
                table: "Barbershops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperationalStatus",
                table: "Barbershops",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastVerifiedAt",
                table: "Barbershops");

            migrationBuilder.DropColumn(
                name: "OperationalStatus",
                table: "Barbershops");
        }
    }
}
