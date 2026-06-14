using WebApplication1.Models.GooglePlaces;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services
{
    /// <summary>
    /// Abstraction over the Google Places API.
    /// Provides live Nearby Search, full place details (photos, reviews, hours), and a
    /// multi-layer cache (memory → DB → live API) for place detail requests.
    /// </summary>
    public interface IGooglePlacesService
    {
        /// <summary>
        /// Fetches place details (rating, reviews, maps URL) for a given Google Places placeId.
        /// Results are cached in memory (1 hour) and in the database (24 hours).
        /// Returns null if the placeId is empty.
        /// </summary>
        /// <param name="placeId">The Google Places placeId string (e.g. "ChIJ...").</param>
        /// <returns>A <see cref="GooglePlacesResult"/> or null.</returns>
        Task<GooglePlacesResult?> GetPlaceDetailsAsync(string placeId);

        /// <summary>
        /// Fetches full place details including photos, opening hours, phone, website, address,
        /// geometry and reviews. Used to populate the interactive map detail panel.
        /// Results are NOT persisted to DB cache (photos/hours change frequently).
        /// </summary>
        /// <param name="placeId">The Google Places placeId string (e.g. "ChIJ...").</param>
        /// <returns>A <see cref="PlaceDetailsResult"/> or null if placeId is empty.</returns>
        Task<PlaceDetailsResult?> GetFullPlaceDetailsAsync(string placeId);

        /// <summary>
        /// Searches for barbershops near the given coordinates using the Google Places Nearby Search API
        /// with <c>type=hair_care</c>. Falls back to an empty list when the API key is unavailable.
        /// </summary>
        /// <param name="lat">Centre latitude.</param>
        /// <param name="lng">Centre longitude.</param>
        /// <param name="radiusMeters">Search radius in metres (max 50 000).</param>
        /// <returns>A list of <see cref="Models.ViewModels.BarberShopPlaceViewModel"/> ready for map rendering.</returns>
        Task<IEnumerable<Models.ViewModels.BarberShopPlaceViewModel>> SearchNearbyBarbershopsAsync(double lat, double lng, int radiusMeters);

        /// <summary>
        /// Performs a live dual-type Nearby Search (type=hair_care AND type=barber) concurrently,
        /// merges and deduplicates the results by place_id, and returns a combined list.
        /// This is the primary method for the <c>GET /Map/GetLiveMarkers</c> endpoint.
        /// Returns an empty collection when the API key is absent or both requests fail.
        /// Results are cached in memory for 10 minutes per lat/lng/radius combination.
        /// </summary>
        /// <param name="lat">Centre latitude.</param>
        /// <param name="lng">Centre longitude.</param>
        /// <param name="radiusInMeters">Search radius in metres (clamped to 50 000).</param>
        /// <returns>A deduplicated, merged <see cref="IReadOnlyList{BarberShopPlaceViewModel}"/>.</returns>
        Task<IReadOnlyList<Models.ViewModels.BarberShopPlaceViewModel>> FetchLiveBarbershopsAsync(double lat, double lng, int radiusInMeters, string? query = null);

        /// <summary>
        /// Calls the Google Places Details API for the given <paramref name="placeId"/> and reads
        /// the <c>business_status</c> field to determine whether the establishment is still operational.
        /// Maps Google's status string to our internal <see cref="Models.OperationalStatus"/> enum.
        /// Returns <see cref="PlaceVerificationResult"/> with <c>Status = Unverified</c> when the
        /// API key is absent, the call fails, or the placeId is empty.
        /// </summary>
        /// <param name="placeId">Google Places ID to verify.</param>
        /// <returns>A <see cref="PlaceVerificationResult"/> describing the current operational status.</returns>
        Task<PlaceVerificationResult> VerifyPlaceStatusAsync(string placeId);
    }

    /// <summary>
    /// DTO returned by <see cref="IGooglePlacesService.GetPlaceDetailsAsync"/>.
    /// </summary>
    public class GooglePlacesResult
    {
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string? GoogleMapsUrl { get; set; }
        public List<GoogleReviewItem> Reviews { get; set; } = new();

        /// <summary>True when data was served from the persistent DB cache.</summary>
        public bool FromDbCache { get; set; }

        /// <summary>True when data was served from in-process memory cache.</summary>
        public bool FromMemoryCache { get; set; }

    }

    /// <summary>
    /// Result of a <see cref="IGooglePlacesService.VerifyPlaceStatusAsync"/> call.
    /// </summary>
    public class PlaceVerificationResult
    {
        /// <summary>Our internal mapped status.</summary>
        public Models.OperationalStatus Status { get; set; } = Models.OperationalStatus.Unverified;

        /// <summary>Raw <c>business_status</c> string from Google (e.g. "OPERATIONAL").</summary>
        public string? RawBusinessStatus { get; set; }

        /// <summary>True when the result came from a live API call (false = fallback/error).</summary>
        public bool IsLive { get; set; }

        /// <summary>Human-readable error message when <see cref="IsLive"/> is false.</summary>
        public string? ErrorMessage { get; set; }
    }
}
