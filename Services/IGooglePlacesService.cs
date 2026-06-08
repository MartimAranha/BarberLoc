using WebApplication1.Models.GooglePlaces;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services
{
    /// <summary>
    /// Abstraction over the Google Places Details API.
    /// Implementations must support both live API calls and graceful fallback to mock data.
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
        /// Falls back to a rich mock result when the API is unavailable.
        /// </summary>
        /// <param name="placeId">The Google Places placeId string (e.g. "ChIJ...").</param>
        /// <returns>A <see cref="PlaceDetailsResult"/> or null if placeId is empty.</returns>
        Task<PlaceDetailsResult?> GetFullPlaceDetailsAsync(string placeId);

        /// <summary>
        /// Searches for barbershops near the given coordinates using the Google Places Nearby Search API.
        /// Falls back to an empty list when the API key is unavailable — the map will then show only seeded records.
        /// </summary>
        /// <param name="lat">Centre latitude.</param>
        /// <param name="lng">Centre longitude.</param>
        /// <param name="radiusMeters">Search radius in metres (max 50000).</param>
        /// <returns>A list of <see cref="Models.ViewModels.BarberShopPlaceViewModel"/> ready for map rendering.</returns>
        Task<IEnumerable<Models.ViewModels.BarberShopPlaceViewModel>> SearchNearbyBarbershopsAsync(double lat, double lng, int radiusMeters);
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

        /// <summary>True when the live API was unavailable and mock data was returned.</summary>
        public bool IsMockData { get; set; }
    }
}
