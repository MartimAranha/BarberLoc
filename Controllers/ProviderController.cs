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
            int radius = (vm.RadiusInKm > 0 ? vm.RadiusInKm : 15) * 1000;

            // Focus the Google Text Search query on the active category filter so that
            // Google's own ranking biases results toward the correct type before we filter.
            string searchQuery;
            if (!string.IsNullOrWhiteSpace(vm.SearchQuery))
            {
                searchQuery = vm.SearchQuery;
            }
            else
            {
                searchQuery = vm.ServiceGender switch
                {
                    "Cabeleireiro" => "cabeleireiro salão de beleza",
                    "Barbearia"    => "barbearia barber shop",
                    _              => "barbearia cabeleireiro salão de beleza"
                };
            }

            // Fetch live Google Places data — Category is set authoritatively by the service.
            var liveResults = await _googlePlaces.FetchLiveBarbershopsAsync(lat, lng, radius, searchQuery);

            var mappedResults = liveResults.Select(r => new Barbershop
            {
                Id = 0, // Live results have no DB ID
                GooglePlaceId = r.PlaceId,
                Name = r.Name,
                Address = r.Address ?? "Sem morada",
                Latitude = r.Lat,
                Longitude = r.Lng,
                ImageUrl = r.PhotoUrl,
                // InferCategory maps the authoritative string set by GooglePlacesService.
                Category = r.Category switch
                {
                    "HairSalon" => BarbershopCategory.HairSalon,
                    "Unisex"    => BarbershopCategory.Unisex,
                    _           => BarbershopCategory.Barbershop
                },
                Rating = r.Rating,
                UserRatingsTotal = r.UserRatingsTotal,
                Services = new List<Service>()
            }).AsEnumerable();

            // ── Filter: pure Category-based predicates — no name-keyword heuristics ────
            if (vm.MinRating.HasValue)
            {
                mappedResults = mappedResults.Where(r => r.Rating >= vm.MinRating.Value);
            }

            if (!string.IsNullOrEmpty(vm.ServiceGender) && vm.ServiceGender != "Todos")
            {
                mappedResults = vm.ServiceGender switch
                {
                    "Barbearia"    => mappedResults.Where(r => r.Category == BarbershopCategory.Barbershop),
                    "Cabeleireiro" => mappedResults.Where(r => r.Category == BarbershopCategory.HairSalon),
                    _              => mappedResults
                };
            }

            // ── Sort ──────────────────────────────────────────────────────────
            var allResults = vm.SortBy switch
            {
                "name"   => mappedResults.OrderBy(b => b.Name).ToList(),
                "rating" => mappedResults.OrderByDescending(b => b.Rating ?? 0).ToList(),
                "newest" => mappedResults.OrderByDescending(b => b.CreatedAt).ToList(),
                _        => mappedResults.OrderByDescending(b => b.Rating ?? 0).ToList()
            };

            vm.Results = allResults;
            vm.TotalCount = allResults.Count;

            return View(vm);
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
