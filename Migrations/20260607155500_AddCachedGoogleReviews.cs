using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedGoogleReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CachedGoogleReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlaceId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ReviewsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GoogleRating = table.Column<double>(type: "float", nullable: true),
                    UserRatingsTotal = table.Column<int>(type: "int", nullable: true),
                    GoogleMapsUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedGoogleReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CachedGoogleReviews_PlaceId",
                table: "CachedGoogleReviews",
                column: "PlaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CachedGoogleReviews");
        }
    }
}
