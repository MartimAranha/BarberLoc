using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;
using WebApplication1.Models.GooglePlaces;

namespace WebApplication1.Controllers
{
    /// <summary>
    /// Provider search and discovery controller.
    /// Handles the Uber-style listing page and rich profile details page.
    /// Routes: /Provider/Index, /Provider/Details/{id}
    /// </summary>
    public class ProviderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IGooglePlacesService _googlePlaces;
        private readonly IMemoryCache _cache;

        public ProviderController(
            ApplicationDbContext context,
            IGooglePlacesService googlePlaces,
            IMemoryCache cache)
        {
            _context = context;
            _googlePlaces = googlePlaces;
            _cache = cache;
        }

        // ── GET /Provider ──────────────────────────────────────────────────────

        /// <summary>
        /// Search and listing page. Accepts filter parameters via GET query string
        /// so results are bookmarkable/shareable.
        /// </summary>
        public async Task<IActionResult> Index(ProviderSearchViewModel vm)
        {
            // Default search fallback using Lisbon coordinates
            double lat = 38.7223;
            double lng = -9.1393;
            int radius = 15000; // 15km default radius

            var searchQuery = string.IsNullOrWhiteSpace(vm.SearchQuery) ? "Barbearia" : vm.SearchQuery;

            // Fetch live Google Places data
            var liveResults = await _googlePlaces.FetchLiveBarbershopsAsync(lat, lng, radius, searchQuery);

            var mappedResults = liveResults.Select(r => new Barbershop
            {
                Id = 0, // Map live results with no DB ID
                GooglePlaceId = r.PlaceId,
                Name = r.Name,
                Address = r.Address ?? "Sem morada",
                Latitude = r.Lat,
                Longitude = r.Lng,
                ImageUrl = r.PhotoUrl, // Mapped to the proxy photo URL
                Category = InferCategory(r.Category),
                Services = new List<Service>() // Real services would require the DB
            }).ToList();

            // ── Sort ──────────────────────────────────────────────────────────
            var allResults = vm.SortBy switch
            {
                "name" => mappedResults.OrderBy(b => b.Name).ToList(),
                _ => mappedResults.OrderBy(b => b.Name).ToList() // default: name
            };

            vm.Results = allResults;
            vm.TotalCount = allResults.Count;

            return View(vm);
        }

        private static BarbershopCategory InferCategory(string? category)
        {
            if (category == "HairSalon") return BarbershopCategory.HairSalon;
            if (category == "Unisex") return BarbershopCategory.Unisex;
            return BarbershopCategory.Barbershop;
        }

        // ── GET /Provider/Details/{id} ─────────────────────────────────────────

        /// <summary>
        /// Rich profile page for a single provider. Loads data directly from Google Places API.
        /// </summary>
        [Route("Provider/Details/{id}")]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "0")
                return NotFound("Place ID inválido.");

            // Fetch full extended details directly from Google Places API
            var googleData = await _googlePlaces.GetFullPlaceDetailsAsync(id);
            
            if (googleData == null)
                return NotFound("Não foi possível encontrar detalhes para este local no Google Places.");

            var vm = new PlaceDetailsViewModel
            {
                PlaceId = googleData.PlaceId,
                Name = googleData.Name,
                FormattedAddress = googleData.FormattedAddress,
                PhoneNumber = googleData.FormattedPhoneNumber ?? googleData.InternationalPhoneNumber,
                Website = googleData.Website,
                Rating = googleData.Rating,
                UserRatingsTotal = googleData.UserRatingsTotal,
                GoogleMapsUrl = googleData.GoogleMapsUrl,
                IsOpenNow = googleData.OpeningHours?.IsOpenNow,
                WeekdayText = googleData.OpeningHours?.WeekdayText ?? new List<string>(),
                Photos = googleData.Photos,
                Reviews = googleData.Reviews,
                Lat = googleData.Lat,
                Lng = googleData.Lng
            };

            return View(vm);
        }
    }
}
