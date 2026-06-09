using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{
    /// <summary>
    /// Represents a barbershop/hair salon entry sourced from the Google Places API.
    /// Used to cache nearby search results and seed the map without requiring a live API call on every page load.
    /// The <see cref="PlaceId"/> is the authoritative Google Places identifier used to fetch full details.
    /// </summary>
    public class BarberShopPlace
    {
        public int Id { get; set; }

        /// <summary>Google Places unique identifier (e.g. "ChIJ..."). Must be unique across all records.</summary>
        [Required]
        [StringLength(200)]
        public string PlaceId { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Address { get; set; }

        [Phone]
        [StringLength(30)]
        public string? PhoneNumber { get; set; }

        [Url]
        [StringLength(500)]
        public string? Website { get; set; }

        /// <summary>Google Places average rating (1.0–5.0).</summary>
        public double? Rating { get; set; }

        /// <summary>Total number of Google user ratings.</summary>
        public int? UserRatingsTotal { get; set; }

        /// <summary>WGS-84 latitude of the place.</summary>
        public double Latitude { get; set; }

        /// <summary>WGS-84 longitude of the place.</summary>
        public double Longitude { get; set; }

        /// <summary>JSON-serialised list of weekday opening hours strings from the Places API.</summary>
        public string? OpeningHoursJson { get; set; }

        /// <summary>
        /// The first photo_reference token from the Places API.
        /// Use <c>/Barbershops/PlacePhoto?ref={PhotoReference}</c> to serve it via the server-side proxy.
        /// </summary>
        [StringLength(500)]
        public string? PhotoReference { get; set; }

        /// <summary>UTC timestamp of the last successful Places API fetch. Used to decide cache staleness.</summary>
        public DateTime LastFetchedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Category of this barbershop (Barbershop, HairSalon, Unisex).
        /// Stored here so the map can colour markers without a join to the Barbershops table on every page load.
        /// Defaults to <see cref="BarbershopCategory.Barbershop"/>.
        /// </summary>
        public BarbershopCategory Category { get; set; } = BarbershopCategory.Barbershop;
    }
}
