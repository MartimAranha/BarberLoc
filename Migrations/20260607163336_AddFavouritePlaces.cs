using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddFavouritePlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavouritePlaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlaceId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PlaceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlaceAddress = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SavedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavouritePlaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavouritePlaces_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavouritePlaces_UserId_PlaceId",
                table: "FavouritePlaces",
                columns: new[] { "UserId", "PlaceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavouritePlaces");
        }
    }
}
