using WebApplication1.Models.GooglePlaces;

namespace WebApplication1.Models.ViewModels
{
    /// <summary>
    /// Consolidated ViewModel that merges a <see cref="Barbershop"/> database record with
    /// live data fetched via <see cref="Services.IGooglePlacesService.GetFullPlaceDetailsAsync"/>.
    /// Returned by <c>MapController.GetDetails(int id)</c> as JSON for the AJAX offcanvas panel.
    /// </summary>
    public class BarbershopDetailsViewModel
    {
        // ── Identity ────────────────────────────────────────────────────────────

        /// <summary>Primary key from the <see cref="Barbershop"/> table.</summary>
        public int Id { get; set; }

        /// <summary>Google Places identifier used for live API calls.</summary>
        public string? GooglePlaceId { get; set; }

        // ── Core DB Fields ───────────────────────────────────────────────────────

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Website { get; set; }
        public BarbershopCategory Category { get; set; }

        // ── Live Google Places Data (nullable — unavailable when API key absent) ─

        /// <summary>Live Google star rating (1.0–5.0). Null when no API key or API failure.</summary>
        public double? GoogleRating { get; set; }

        /// <summary>Total number of Google user ratings.</summary>
        public int? UserRatingsTotal { get; set; }

        /// <summary>Google Maps deep-link for the CTA button. Never used for auto-redirect.</summary>
        public string? GoogleMapsUrl { get; set; }
        
        /// <summary>Formatted phone number from Google Places.</summary>
        public string? FormattedPhoneNumber { get; set; }
        
        /// <summary>International phone number from Google Places.</summary>
        public string? InternationalPhoneNumber { get; set; }

        /// <summary>True if the place is currently open, according to the Places API.</summary>
        public bool? IsOpenNow { get; set; }

        /// <summary>Weekday opening hours strings, e.g. "Segunda-feira: 09:00 – 20:00".</summary>
        public List<string> WeekdayText { get; set; } = new();

        /// <summary>Up to 5 photo references; proxy URLs are resolved server-side.</summary>
        public List<PlacePhotoViewModel> Photos { get; set; } = new();

        /// <summary>Up to 5 Google reviewer entries.</summary>
        public List<PlaceReviewViewModel> Reviews { get; set; } = new();

        // ── Meta ─────────────────────────────────────────────────────────────────


        /// <summary>True when the current authenticated user has favourited this place.</summary>
        public bool IsFavourited { get; set; }

        /// <summary>Google API Key to pass securely to the client script tag.</summary>
        public string GoogleApiKey { get; set; } = string.Empty;

        // ── Local DB Relations ───────────────────────────────────────────────────
        
        public List<Service> Services { get; set; } = new();
    }

    /// <summary>Photo reference DTO — proxy URL is resolved server-side.</summary>
    public class PlacePhotoViewModel
    {
        public int Index { get; set; }

        /// <summary>Server-side proxy URL (e.g. <c>/Barbershops/PlacePhoto?ref=...&amp;maxWidth=800</c>).</summary>
        public string ProxyUrl { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>Reviewer DTO for the panel review list.</summary>
    public class PlaceReviewViewModel
    {
        public string AuthorName { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }
        public int Rating { get; set; }
        public string? RelativeTimeDescription { get; set; }
        public string? Text { get; set; }
    }
}
