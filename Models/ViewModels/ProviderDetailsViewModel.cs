namespace WebApplication1.Models.ViewModels
{
    /// <summary>
    /// ViewModel for the Provider details page (Provider/Details).
    /// Wraps the Barbershop entity and enriches it with cached Google Places data.
    /// </summary>
    public class ProviderDetailsViewModel
    {
        // ── Core Entity ────────────────────────────────────────────────────────
        public Barbershop Barbershop { get; set; } = null!;

        // ── Google Places Enrichment ───────────────────────────────────────────
        /// <summary>Overall Google rating fetched from Places API.</summary>
        public double? GoogleRating { get; set; }

        /// <summary>Total number of Google user ratings.</summary>
        public int? GoogleUserRatingsTotal { get; set; }

        /// <summary>Direct link to Google Maps page for this business.</summary>
        public string? GoogleMapsUrl { get; set; }

        /// <summary>Parsed Google reviewer objects for the review tab.</summary>
        public List<GoogleReviewItem> GoogleReviews { get; set; } = new();

        /// <summary>Whether Google Places data was successfully loaded.</summary>
        public bool HasGoogleData => GoogleReviews.Any() || GoogleRating.HasValue;

        /// <summary>Whether the Google Places service is in mock/demo mode.</summary>
        public bool IsGoogleDataMock { get; set; } = false;

        // ── Convenience passthrough properties ─────────────────────────────────
        public int BarbershopId => Barbershop.Id;
        public string BarbershopName => Barbershop.Name;
        public ICollection<Service> Services => Barbershop.Services;
        public ICollection<Review> LocalReviews => Barbershop.Reviews;
    }

    /// <summary>
    /// A single Google reviewer entry deserialized from cached JSON.
    /// </summary>
    public class GoogleReviewItem
    {
        public string AuthorName { get; set; } = string.Empty;
        public string? AuthorPhotoUrl { get; set; }
        public int Rating { get; set; }
        public string? Text { get; set; }
        public string? RelativeTimeDescription { get; set; }
        public long? Time { get; set; }
    }
}
