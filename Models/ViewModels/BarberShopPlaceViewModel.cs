using System.Text.Json.Serialization;

namespace WebApplication1.Models.ViewModels
{
    /// <summary>
    /// Flat, JSON-serialisation-safe ViewModel for a single barbershop on the map.
    /// Mapped from <see cref="BarberShopPlace"/> by <c>MapController</c>.
    /// Serialised directly into a JavaScript variable via <c>Json.Serialize(Model.NearbyShops)</c>.
    /// All property names are camelCase to match JS convention.
    /// </summary>
    public class BarberShopPlaceViewModel
    {
        [JsonPropertyName("placeId")]
        public string PlaceId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("phoneNumber")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("website")]
        public string? Website { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("userRatingsTotal")]
        public int? UserRatingsTotal { get; set; }

        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }

        [JsonPropertyName("openingHoursJson")]
        public string? OpeningHoursJson { get; set; }

        [JsonPropertyName("photoReference")]
        public string? PhotoReference { get; set; }

        /// <summary>
        /// Category string used by the map JS to look up marker colour.
        /// Values: "Barbershop" | "HairSalon" | "Unisex".
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = "Barbershop";

        /// <summary>
        /// True when the application is running in Demo Mode (no live API key configured).
        /// The JS uses this to show an inline demo badge on markers with no real PlaceId.
        /// </summary>
        [JsonPropertyName("isDemoMode")]
        public bool IsDemoMode { get; set; }

        // ── Computed display properties ─────────────────────────────────────────

        /// <summary>Human-readable rating string, e.g. "4.5 ★". Empty string when no rating.</summary>
        [JsonPropertyName("formattedRating")]
        public string FormattedRating =>
            Rating.HasValue ? $"{Rating:F1} ★" : string.Empty;

        /// <summary>
        /// Server-side photo proxy URL for the first photo reference.
        /// Keeps the Google API key off the client.
        /// Null when no photo reference is stored.
        /// </summary>
        [JsonPropertyName("photoUrl")]
        public string? PhotoUrl =>
            string.IsNullOrWhiteSpace(PhotoReference)
                ? null
                : $"/Barbershops/PlacePhoto?ref={Uri.EscapeDataString(PhotoReference)}&maxWidth=800";
    }
}
