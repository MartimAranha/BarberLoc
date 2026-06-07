using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{
    /// <summary>
    /// Stores fetched Google Places review/detail data to avoid repeated API calls.
    /// Cache entries expire after 24 hours and are refreshed on next access.
    /// </summary>
    public class CachedGoogleReview
    {
        public int Id { get; set; }

        /// <summary>Google Places placeId for the business.</summary>
        [Required]
        [StringLength(300)]
        public string PlaceId { get; set; } = string.Empty;

        /// <summary>Raw JSON payload from the Google Places Details API (or mock).</summary>
        public string ReviewsJson { get; set; } = "[]";

        /// <summary>Overall Google rating (1.0–5.0), cached alongside reviews.</summary>
        public double? GoogleRating { get; set; }

        /// <summary>Total number of Google user ratings.</summary>
        public int? UserRatingsTotal { get; set; }

        /// <summary>Google Maps URL for the business.</summary>
        [StringLength(500)]
        public string? GoogleMapsUrl { get; set; }

        /// <summary>When this cache entry was last populated from the API.</summary>
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Returns true if the cache entry is older than 24 hours.</summary>
        public bool IsExpired => (DateTime.UtcNow - FetchedAt).TotalHours > 24;
    }
}
