using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Models.GooglePlaces;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    /// <summary>
    /// Dedicated controller for the interactive barbershop map page at <c>/Map</c>.
    /// Responsibilities:
    ///   - Serve the initial map view pre-populated with <see cref="BarberShopPlace"/> seed records.
    ///   - Provide <c>GET /Map/GetLiveMarkers</c> — an AJAX endpoint the JS calls on map pan/zoom
    ///     to fetch real-time Google Places data, upsert it to the DB, and return live JSON markers.
    ///   - Provide <c>GET /Map/Details</c> — called by JS on marker click to get full place details
    ///     (photos, reviews, opening hours) without redirecting away from the app.
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

        // ── Live Mode Helper ───────────────────────────────────────────────────
        // Returns true when a valid Places API key is configured, enabling live
        // Google Places Nearby Search calls and dynamic marker refresh on pan/zoom.

        private bool IsLiveMode()
        {
            var key = _config["Google:PlacesApiKey"]
                   ?? _config["Google:ApiKey"]
                   ?? string.Empty;
            return !string.IsNullOrWhiteSpace(key);
        }

        // GET /Map  or  GET /Map/Index
        // Renders the split-screen map view. Seeded BarberShopPlace records are serialised into
        // the inline JS variable for instant first paint. The JS then calls GetLiveMarkers on
        // pan/zoom to overlay real-time Google Places data on top.
        public async Task<IActionResult> Index()
        {
            var isLive = IsLiveMode();

            // Places API key — kept server-side; never written to client-visible JSON
            var mapsApiKey = _config["GoogleMaps:ApiKey"]
                          ?? _config["Google:PlacesApiKey"]
                          ?? _config["Google:ApiKey"]
                          ?? string.Empty;

            // Load cached/seeded BarberShopPlace records for the initial marker set
            var places = await _context.BarberShopPlaces
                .AsNoTracking()
                .OrderByDescending(p => p.Rating)
                .ToListAsync();

            var shopViewModels = places.Select(p => new BarberShopPlaceViewModel
            {
                PlaceId          = p.PlaceId,
                Name             = p.Name,
                Address          = p.Address,
                PhoneNumber      = p.PhoneNumber,
                Website          = p.Website,
                Rating           = p.Rating,
                UserRatingsTotal = p.UserRatingsTotal,
                Lat              = p.Latitude,
                Lng              = p.Longitude,
                OpeningHoursJson = p.OpeningHoursJson,
                PhotoReference   = p.PhotoReference,
                Category         = p.Category.ToString(),
                IsDemoMode       = false   // always false — live mode only
            });

            var viewModel = new MapPageViewModel
            {
                NearbyShops      = shopViewModels,
                GoogleMapsApiKey = mapsApiKey,
                DefaultLatitude  = 38.7169,
                DefaultLongitude = -9.1399,
                DefaultZoom      = 14,
                HasApiKey        = isLive,
                IsDemoMode       = false   // demo mode permanently disabled
            };

            return View(viewModel);
        }

        // GET /Map/GetMarkers
        // Lightweight AJAX endpoint — returns cached/seeded BarberShopPlace records as flat JSON.
        // Supports optional server-side filtering via `category` and `minRating` query-string params.
        // The Google API key is never included in this response.
        [HttpGet]
        public async Task<IActionResult> GetMarkers(string? category = null, double? minRating = null)
        {
            var query = _context.BarberShopPlaces.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(category) &&
                Enum.TryParse<BarbershopCategory>(category, ignoreCase: true, out var parsedCat))
                query = query.Where(p => p.Category == parsedCat);

            if (minRating.HasValue)
                query = query.Where(p => p.Rating >= minRating.Value);

            var places = await query
                .OrderByDescending(p => p.Rating)
                .ToListAsync();

            var markers = places.Select(p => new
            {
                placeId          = p.PlaceId,
                name             = p.Name,
                address          = p.Address,
                phoneNumber      = p.PhoneNumber,
                website          = p.Website,
                rating           = p.Rating,
                userRatingsTotal = p.UserRatingsTotal,
                lat              = p.Latitude,
                lng              = p.Longitude,
                category         = p.Category.ToString(),
                isDemoMode       = false
            });

            return Json(markers);
        }

        // GET /Map/GetLiveMarkers?lat=…&lng=…&radius=…
        // Primary AJAX endpoint called by the Leaflet map on every dragend/zoomend event.
        // Fetches real-time barbershop data from Google Places (dual-type: hair_care + barber),
        // upserts results into BarberShopPlace (cache) and Barbershop (bookings) tables,
        // then returns the merged, deduplicated list as JSON for the JS to render as markers.
        // All upserts are guarded by PlaceId uniqueness — safe to call on every pan.
        [HttpGet]
        public async Task<IActionResult> GetLiveMarkers(double lat, double lng, int radius = 1500)
        {
            // Validate coordinates
            if (lat is < -90 or > 90 || lng is < -180 or > 180)
                return BadRequest(new { success = false, message = "Invalid coordinates." });

            radius = Math.Clamp(radius, 100, 50_000);

            // ── 1. Fetch live results from Google Places API (dual-type, cached 10 min) ─
            var liveResults = await _placesService.FetchLiveBarbershopsAsync(lat, lng, radius);

            if (liveResults.Count == 0)
            {
                // No live data — serve whatever is cached in BarberShopPlaces table
                var cached = await _context.BarberShopPlaces
                    .AsNoTracking()
                    .OrderByDescending(p => p.Rating)
                    .Select(p => new
                    {
                        placeId          = p.PlaceId,
                        name             = p.Name,
                        address          = p.Address,
                        rating           = p.Rating,
                        userRatingsTotal = p.UserRatingsTotal,
                        lat              = p.Latitude,
                        lng              = p.Longitude,
                        category         = p.Category.ToString(),
                        isDemoMode       = false,
                        isLive           = false
                    })
                    .ToListAsync();

                return Json(cached);
            }

            // ── 2. Upsert into BarberShopPlace (map cache) and Barbershop (bookings) ──
            // Collect existing PlaceIds in a single DB round-trip to minimise queries
            var incomingIds = liveResults.Select(r => r.PlaceId).ToList();

            var existingPlaces = await _context.BarberShopPlaces
                .Where(b => incomingIds.Contains(b.PlaceId))
                .Select(b => b.PlaceId)
                .ToListAsync();
            var existingPlacesSet = existingPlaces != null
                ? new HashSet<string>(existingPlaces)
                : new HashSet<string>();

            var existingBarbershops = await _context.Barbershops
                .Where(b => b.GooglePlaceId != null && incomingIds.Contains(b.GooglePlaceId))
                .Select(b => b.GooglePlaceId!)
                .ToListAsync();
            var existingBarbershopsSet = existingBarbershops != null
                ? new HashSet<string>(existingBarbershops)
                : new HashSet<string>();

            var now = DateTime.UtcNow;

            foreach (var vm in liveResults)
            {
                if (string.IsNullOrWhiteSpace(vm.PlaceId)) continue;

                // ── Upsert BarberShopPlace (map cache table) ──────────────────────────
                if (!existingPlacesSet.Contains(vm.PlaceId))
                {
                    // Determine category heuristically from name (Google tags are unreliable for category)
                    var inferredCategory = InferCategory(vm.Name);

                    _context.BarberShopPlaces.Add(new BarberShopPlace
                    {
                        PlaceId          = vm.PlaceId,
                        Name             = vm.Name,
                        Address          = vm.Address,
                        Rating           = vm.Rating,
                        UserRatingsTotal = vm.UserRatingsTotal,
                        Latitude         = vm.Lat,
                        Longitude        = vm.Lng,
                        PhotoReference   = vm.PhotoReference,
                        Category         = inferredCategory,
                        LastFetchedAt    = now
                    });
                }
                else
                {
                    // Refresh rating and photo reference on subsequent fetches
                    await _context.BarberShopPlaces
                        .Where(b => b.PlaceId == vm.PlaceId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.Rating,           vm.Rating)
                            .SetProperty(b => b.UserRatingsTotal, vm.UserRatingsTotal)
                            .SetProperty(b => b.PhotoReference,   vm.PhotoReference)
                            .SetProperty(b => b.LastFetchedAt,    now));
                }

                // ── Upsert Barbershop (enables bookings & reviews on live places) ─────
                if (!existingBarbershopsSet.Contains(vm.PlaceId))
                {
                    _context.Barbershops.Add(new Barbershop
                    {
                        Name          = vm.Name,
                        Address       = vm.Address ?? string.Empty,
                        Latitude      = vm.Lat,
                        Longitude     = vm.Lng,
                        PhoneNumber   = vm.PhoneNumber,
                        AverageRating = vm.Rating ?? 0,
                        GooglePlaceId = vm.PlaceId, // FIX: set GooglePlaceId as required
                        PlaceId       = vm.PlaceId, // legacy fallback
                        Category      = InferCategory(vm.Name),
                        IsActive      = true,
                        CreatedAt     = now
                    });
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetLiveMarkers: DB upsert failed — returning live results without persistence.");
            }

            // ── 3. Return the live marker list as JSON ────────────────────────────────
            var response = liveResults.Select(vm => new
            {
                placeId          = vm.PlaceId,
                name             = vm.Name,
                address          = vm.Address,
                rating           = vm.Rating,
                userRatingsTotal = vm.UserRatingsTotal,
                lat              = vm.Lat,
                lng              = vm.Lng,
                category         = InferCategory(vm.Name).ToString(),
                photoUrl         = vm.PhotoUrl,
                isDemoMode       = false,
                isLive           = true
            });

            return Json(response);
        }

        // GET /Map/GetDetails/{id}
        // AJAX-only endpoint called by Map/Index.cshtml JS when the user clicks a BarberShopPlace marker.
        // Fetches the Barbershop record from DB by integer ID, calls GooglePlacesService for live data,
        // and returns a fully consolidated BarbershopDetailsViewModel as JSON.
        // Priority for reviews: Google API → local DB Reviews → empty list.
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

            // ── Resolve photos ─────────────────────────────────────────────────
            // Proxy URLs resolved server-side — API key never sent to client
            var photos = (googleData?.Photos ?? new List<PlacePhoto>())
                .Select((p, i) => new PlacePhotoViewModel
                {
                    Index    = i,
                    ProxyUrl = p.GetProxyUrl(800),
                    Width    = p.Width,
                    Height   = p.Height
                }).ToList();

            // ── Resolve reviews ────────────────────────────────────────────────
            // Priority: Google API reviews → local DB reviews (for places without PlaceId)
            List<PlaceReviewViewModel> reviews;

            if (googleData?.Reviews is { Count: > 0 })
            {
                // Live Google reviews — the primary source in live mode
                reviews = googleData.Reviews
                    .Select(r => new PlaceReviewViewModel
                    {
                        AuthorName              = r.AuthorName,
                        ProfilePhotoUrl         = r.ProfilePhotoUrl,
                        Rating                  = r.Rating,
                        RelativeTimeDescription = r.RelativeTimeDescription,
                        Text                    = r.Text
                    }).ToList();
            }
            else
            {
                // Fallback: load local DB reviews (useful for seeded places or when Google returns no reviews)
                var dbReviews = await _context.Reviews
                    .AsNoTracking()
                    .Include(r => r.User)
                    .Where(r => r.BarbershopId == barbershop.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                reviews = dbReviews.Select(r => new PlaceReviewViewModel
                {
                    AuthorName              = r.User?.FullName ?? "Utilizador",
                    ProfilePhotoUrl         = null,
                    Rating                  = r.Rating,
                    RelativeTimeDescription = FormatRelativeTime(r.CreatedAt),
                    Text                    = r.Comment
                }).ToList();
            }

            // ── Check favourited state for authenticated users ──────────────────
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
                isDemoMode           = false,
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
        // Returns full live place details as JSON. Never redirects. Google API key never leaves the server.
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
                    isDemoMode           = false,
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

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Converts an absolute <see cref="DateTime"/> to a human-readable Portuguese relative string,
        /// e.g. "há 3 dias", "há 2 semanas", "há 1 mês".
        /// </summary>
        private static string FormatRelativeTime(DateTime createdAt)
        {
            var delta = DateTime.Now - createdAt;
            return delta.TotalDays switch
            {
                < 1    => "hoje",
                < 2    => "ontem",
                < 7    => $"há {(int)delta.TotalDays} dias",
                < 14   => "há 1 semana",
                < 30   => $"há {(int)(delta.TotalDays / 7)} semanas",
                < 60   => "há 1 mês",
                < 365  => $"há {(int)(delta.TotalDays / 30)} meses",
                _      => $"há {(int)(delta.TotalDays / 365)} anos"
            };
        }

        /// <summary>
        /// Infers <see cref="BarbershopCategory"/> from a place name.
        /// Google's Nearby Search type field is broad (hair_care covers both barbers and salons),
        /// so we use the name as a heuristic to set a meaningful category for marker colouring.
        /// </summary>
        private static BarbershopCategory InferCategory(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("cabeleireiro") || lower.Contains("salão") ||
                lower.Contains("salon")        || lower.Contains("beauty") ||
                lower.Contains("spa")          || lower.Contains("nails")  ||
                lower.Contains("unhas"))
                return BarbershopCategory.HairSalon;

            if (lower.Contains("unisex") || lower.Contains("hair"))
                return BarbershopCategory.Unisex;

            return BarbershopCategory.Barbershop;
        }
    }
}
