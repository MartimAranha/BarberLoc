using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.GooglePlaces;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    /// <summary>
    /// Dedicated controller for the interactive barbershop map page at <c>/Map</c>.
    /// Responsibilities:
    ///   - Serve the initial map view pre-populated with <see cref="BarberShopPlace"/> seed records.
    ///   - Provide an AJAX endpoint (<c>GET /Map/Details</c>) that the JS calls on marker click
    ///     to retrieve full place details without redirecting to Google Maps.
    /// </summary>
    public class MapController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IGooglePlacesService _placesService;
        private readonly ILogger<MapController> _logger;

        public MapController(
            ApplicationDbContext context,
            IConfiguration config,
            IGooglePlacesService placesService,
            ILogger<MapController> logger)
        {
            _context = context;
            _config = config;
            _placesService = placesService;
            _logger = logger;
        }

        // GET /Map  or  GET /Map/Index
        // Renders the map view with all BarberShopPlace records serialised into the JS variable.
        // The Google Maps API key is passed via the ViewModel — it is injected into the <script src> tag
        // server-side and never written into client-visible JSON.
        public async Task<IActionResult> Index()
        {
            // Read API key from config — try both key paths for compatibility with existing config
            var apiKey = _config["GoogleMaps:ApiKey"]
                      ?? _config["Google:PlacesApiKey"]
                      ?? _config["Google:ApiKey"]
                      ?? string.Empty;

            // Fetch all seeded / cached BarberShopPlace records from the DB
            var places = await _context.BarberShopPlaces
                .AsNoTracking()
                .OrderByDescending(p => p.Rating)
                .ToListAsync();

            // Map entity → ViewModel (the ViewModel handles PhotoUrl construction)
            var shopViewModels = places.Select(p => new BarberShopPlaceViewModel
            {
                PlaceId        = p.PlaceId,
                Name           = p.Name,
                Address        = p.Address,
                PhoneNumber    = p.PhoneNumber,
                Website        = p.Website,
                Rating         = p.Rating,
                UserRatingsTotal = p.UserRatingsTotal,
                Lat            = p.Latitude,
                Lng            = p.Longitude,
                OpeningHoursJson = p.OpeningHoursJson,
                PhotoReference = p.PhotoReference
            });

            var viewModel = new MapPageViewModel
            {
                NearbyShops      = shopViewModels,
                GoogleMapsApiKey = apiKey,
                DefaultLatitude  = 38.7169,
                DefaultLongitude = -9.1399
            };

            return View(viewModel);
        }

        // GET /Map/GetDetails/{id}
        // AJAX-only endpoint called by Map/Index.cshtml JS when the user clicks a BarberShopPlace marker.
        // Fetches the Barbershop record from DB by integer ID, calls GooglePlacesService for live data,
        // and returns a fully consolidated BarbershopDetailsViewModel as JSON.
        // Never redirects. Google API key never leaves the server.
        [HttpGet]
        public async Task<IActionResult> GetDetails(int id)
        {
            var barbershop = await _context.Barbershops
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && b.IsActive);

            if (barbershop == null)
                return NotFound(new { success = false, message = "Barbearia não encontrada." });

            PlaceDetailsResult? googleData = null;

            if (!string.IsNullOrWhiteSpace(barbershop.PlaceId))
            {
                try
                {
                    googleData = await _placesService.GetFullPlaceDetailsAsync(barbershop.PlaceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MapController.GetDetails: Google Places call failed for barbershop {Id}.", id);
                }
            }

            // Resolve photo proxy URLs server-side — API key never sent to client
            var photos = (googleData?.Photos ?? new List<WebApplication1.Models.GooglePlaces.PlacePhoto>())
                .Select((p, i) => new PlacePhotoViewModel
                {
                    Index    = i,
                    ProxyUrl = p.GetProxyUrl(800),
                    Width    = p.Width,
                    Height   = p.Height
                }).ToList();

            var reviews = (googleData?.Reviews ?? new List<WebApplication1.Models.GooglePlaces.PlaceReview>())
                .Select(r => new PlaceReviewViewModel
                {
                    AuthorName              = r.AuthorName,
                    ProfilePhotoUrl         = r.ProfilePhotoUrl,
                    Rating                  = r.Rating,
                    RelativeTimeDescription = r.RelativeTimeDescription,
                    Text                    = r.Text
                }).ToList();

            // Check favourited state for authenticated users
            var isFavourited = false;
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrWhiteSpace(barbershop.PlaceId))
                {
                    isFavourited = await _context.FavouritePlaces
                        .AnyAsync(f => f.UserId == userId && f.PlaceId == barbershop.PlaceId);
                }
            }

            var viewModel = new BarbershopDetailsViewModel
            {
                Id            = barbershop.Id,
                GooglePlaceId = barbershop.PlaceId,
                Name          = barbershop.Name,
                Description   = barbershop.Description,
                Address       = barbershop.Address,
                Latitude      = barbershop.Latitude,
                Longitude     = barbershop.Longitude,
                PhoneNumber   = barbershop.PhoneNumber ?? googleData?.FormattedPhoneNumber,
                Email         = barbershop.Email,
                ImageUrl      = barbershop.ImageUrl,
                Website       = googleData?.Website,
                AverageRating = barbershop.AverageRating,
                Category      = barbershop.Category,
                // Live Google Places data (null when API unavailable — JS handles gracefully)
                GoogleRating      = googleData?.Rating,
                UserRatingsTotal  = googleData?.UserRatingsTotal,
                GoogleMapsUrl     = googleData?.GoogleMapsUrl,
                IsOpenNow         = googleData?.OpeningHours?.IsOpenNow,
                WeekdayText       = googleData?.OpeningHours?.WeekdayText ?? new List<string>(),
                Photos            = photos,
                Reviews           = reviews,
                IsMockData        = googleData?.IsMockData ?? false,
                IsFavourited      = isFavourited
            };

            return Json(new
            {
                success              = true,
                id                   = viewModel.Id,
                googlePlaceId        = viewModel.GooglePlaceId,
                isMock               = viewModel.IsMockData,
                name                 = viewModel.Name,
                description          = viewModel.Description,
                formattedAddress     = viewModel.Address,
                formattedPhoneNumber = viewModel.PhoneNumber,
                website              = viewModel.Website,
                rating               = viewModel.GoogleRating ?? (viewModel.AverageRating > 0 ? viewModel.AverageRating : (double?)null),
                userRatingsTotal     = viewModel.UserRatingsTotal,
                googleMapsUrl        = viewModel.GoogleMapsUrl
                                      ?? $"https://maps.google.com/?q={Uri.EscapeDataString(viewModel.Address)}",
                isOpenNow            = viewModel.IsOpenNow,
                weekdayText          = viewModel.WeekdayText,
                photos               = viewModel.Photos,
                reviews              = viewModel.Reviews,
                isFavourited         = viewModel.IsFavourited,
                imageUrl             = viewModel.ImageUrl,
                category             = viewModel.Category.ToString()
            });
        }

        // GET /Map/Details?placeId={placeId}
        // AJAX-only endpoint called by barbershops-map.js when the user clicks a marker.
        // Returns full place details as JSON. Never redirects. No external Google Maps links in the response.
        [HttpGet]
        public async Task<IActionResult> Details(string? placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest(new { success = false, message = "placeId é obrigatório." });

            try
            {
                var result = await _placesService.GetFullPlaceDetailsAsync(placeId);

                if (result == null)
                    return StatusCode(500, new { success = false, message = "Não foi possível obter os detalhes do local." });

                // Resolve photo proxy URLs server-side — the Google API key is never sent to the client.
                // The existing /Barbershops/PlacePhoto proxy endpoint is reused here.
                var photos = result.Photos.Select((p, i) => new
                {
                    index    = i,
                    proxyUrl = p.GetProxyUrl(800),
                    width    = p.Width,
                    height   = p.Height
                }).ToList();

                return Json(new
                {
                    success              = true,
                    isMock               = result.IsMockData,
                    placeId              = result.PlaceId,
                    name                 = result.Name,
                    formattedAddress     = result.FormattedAddress,
                    formattedPhoneNumber = result.FormattedPhoneNumber,
                    website              = result.Website,
                    rating               = result.Rating,
                    userRatingsTotal     = result.UserRatingsTotal,
                    googleMapsUrl        = result.GoogleMapsUrl,   // used only for the CTA button, not for auto-redirect
                    isOpenNow            = result.OpeningHours?.IsOpenNow,
                    weekdayText          = result.OpeningHours?.WeekdayText ?? new List<string>(),
                    photos               = photos,
                    reviews              = result.Reviews.Select(r => new
                    {
                        authorName              = r.AuthorName,
                        profilePhotoUrl         = r.ProfilePhotoUrl,
                        rating                  = r.Rating,
                        relativeTimeDescription = r.RelativeTimeDescription,
                        text                    = r.Text
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MapController.Details failed for placeId {PlaceId}.", placeId);
                return StatusCode(500, new { success = false, message = "Erro interno ao carregar detalhes." });
            }
        }
    }
}
