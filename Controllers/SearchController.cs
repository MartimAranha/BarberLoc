using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    public class SearchController : Controller
    {
        private readonly IGooglePlacesService _placesService;
        private readonly IConfiguration _config;
        private readonly ILogger<SearchController> _logger;

        public SearchController(
            IGooglePlacesService placesService,
            IConfiguration config,
            ILogger<SearchController> logger)
        {
            _placesService = placesService;
            _config = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            var mapsApiKey = _config["Google:PlacesApiKey"]
                          ?? _config["Google:ApiKey"]
                          ?? string.Empty;

            ViewBag.GoogleMapsApiKey = mapsApiKey;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetLiveResults(double lat, double lng, int radius = 1500)
        {
            if (lat == 0 && lng == 0 || lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                lat = 38.7223;
                lng = -9.1393;
            }

            radius = Math.Clamp(radius, 100, 50_000);

            var liveResults = await _placesService.FetchLiveBarbershopsAsync(lat, lng, radius);

            // Category is now set authoritatively by GooglePlacesService.ClassifyCategoryFromTypes.
            // No name-based heuristic needed here — use vm.Category directly.
            var response = liveResults.Select(vm => new BarbershopSearchViewModel
            {
                PlaceId          = vm.PlaceId,
                Name             = vm.Name,
                Address          = vm.Address,
                Rating           = vm.Rating,
                UserRatingsTotal = vm.UserRatingsTotal,
                Latitude         = vm.Lat,
                Longitude        = vm.Lng,
                PhotoUrl         = vm.PhotoUrl,
                Category         = vm.Category  // "Barbershop" | "HairSalon" | "Unisex"
            });

            return Json(response);
        }
    }
}
