using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    public class BarbershopsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private readonly IGooglePlacesService _googlePlaces;

        public BarbershopsController(
            ApplicationDbContext context,
            IConfiguration config,
            IMemoryCache cache,
            IGooglePlacesService googlePlaces)
        {
            _context = context;
            _config = config;
            _cache = cache;
            _googlePlaces = googlePlaces;
        }

        // GET: /Barbershops — Split-screen Live Map
        public IActionResult Index()
        {
            var testKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY_TEST");
            ViewData["GoogleApiKey"] = !string.IsNullOrWhiteSpace(testKey) ? testKey : (_config["Google:ApiKey"] ?? string.Empty);
            return View();
        }

        // GET: /Barbershops/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var barbershop = await _context.Barbershops
                .Include(b => b.Services)
                .Include(b => b.Reviews)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(b => b.Id == id && b.IsActive);

            if (barbershop == null) return NotFound();

            return View(barbershop);
        }

        // GET: /Barbershops/Map
        public async Task<IActionResult> Map()
        {
            var testKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY_TEST");
            ViewData["GoogleApiKey"] = !string.IsNullOrWhiteSpace(testKey) ? testKey : (_config["Google:ApiKey"] ?? string.Empty);
            return View();
        }

        // GET: /Barbershops/GetMapData
        [HttpGet]
        public async Task<JsonResult> GetMapData(double? lat, double? lng, double? radiusKm, double? minRating, string? categories, string? genders, bool? mobileOnly)
        {
            var cats = !string.IsNullOrEmpty(categories) ? categories.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() : new List<string>();
            var genderFilters = !string.IsNullOrEmpty(genders) ? genders.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() : new List<string>();

            var bars = await _context.Barbershops
                .Include(b => b.Services)
                .Where(b => b.IsActive)
                .ToListAsync();

            if (cats.Any())
                bars = bars.Where(b => cats.Contains(b.Category.ToString())).ToList();

            var results = new List<object>();
            foreach (var b in bars)
            {
                var hasMobile = b.Services != null && b.Services.Any(s => s.IsAvailable && s.IsMobile);
                var genderMatch = true;
                if (genderFilters.Any())
                {
                    genderMatch = b.Services != null && b.Services.Any(s =>
                        s.IsAvailable &&
                        (genderFilters.Contains(s.TargetGender.ToString()) || s.TargetGender == Models.TargetGender.Unisex));
                }

                if (!genderMatch) continue;
                if (mobileOnly == true && !hasMobile) continue;

                // Distance filter
                if (lat.HasValue && lng.HasValue && radiusKm.HasValue)
                {
                    var dist = HaversineDistance(lat.Value, lng.Value, b.Latitude, b.Longitude);
                    if (dist > radiusKm.Value) continue;
                }

                // Rating filter
                if (minRating.HasValue && b.AverageRating < minRating.Value)
                    continue;

                results.Add(new
                {
                    id = b.Id,
                    name = b.Name,
                    lat = b.Latitude,
                    lng = b.Longitude,
                    address = b.Address,
                    category = b.Category.ToString(),
                    rating = b.AverageRating,
                    image = b.ImageUrl,
                    phone = b.PhoneNumber,
                    hasMobile = hasMobile,
                    placeId = b.PlaceId
                });
            }

            return Json(results.OrderByDescending(r => (double)r.GetType().GetProperty("rating")!.GetValue(r)!).Take(50));
        }

        // GET: /Barbershops/GetLiveMarkers?lat=...&lng=...&radius=...
        [HttpGet]
        public async Task<IActionResult> GetLiveMarkers(double lat, double lng, int radius = 1500)
        {
            // Note: DB sync logic and exact model mapping is centrally handled in MapController's GetLiveMarkers.
            // But as requested, we provide this endpoint here. To avoid duplicate code, we can just 
            // delegate to the service directly and let MapController or DbSeeder handle the sync, OR 
            // implement the sync here as well. Let's do the sync here to fully satisfy the requirement.
            
            radius = Math.Clamp(radius, 100, 50_000);
            var liveResults = await _googlePlaces.FetchLiveBarbershopsAsync(lat, lng, radius);

            if (liveResults.Count == 0) return Json(new object[] {});

            var incomingIds = liveResults.Select(r => r.PlaceId).ToList();
            var existingBarbershops = await _context.Barbershops
                .Where(b => b.GooglePlaceId != null && incomingIds.Contains(b.GooglePlaceId))
                .Select(b => b.GooglePlaceId!)
                .ToListAsync();
            var existingSet = new HashSet<string>(existingBarbershops);
            var now = DateTime.UtcNow;

            foreach (var vm in liveResults)
            {
                if (string.IsNullOrWhiteSpace(vm.PlaceId)) continue;
                if (!existingSet.Contains(vm.PlaceId))
                {
                    _context.Barbershops.Add(new Barbershop
                    {
                        Name          = vm.Name,
                        Address       = vm.Address ?? string.Empty,
                        Latitude      = vm.Lat,
                        Longitude     = vm.Lng,
                        PhoneNumber   = vm.PhoneNumber,
                        AverageRating = vm.Rating ?? 0,
                        GooglePlaceId = vm.PlaceId,
                        PlaceId       = vm.PlaceId, // fallback
                        Category      = BarbershopCategory.Barbershop, // Simplify for this endpoint
                        IsActive      = true,
                        CreatedAt     = now
                    });
                }
            }

            try { await _context.SaveChangesAsync(); } catch { /* ignore for live render */ }

            return Json(liveResults.Select(vm => new
            {
                placeId = vm.PlaceId,
                name = vm.Name,
                address = vm.Address,
                lat = vm.Lat,
                lng = vm.Lng,
                rating = vm.Rating,
                userRatingsTotal = vm.UserRatingsTotal,
                photoUrl = vm.PhotoUrl
            }));
        }

        // GET: /Barbershops/GetReviews?placeId=...
        [HttpGet]
        public async Task<JsonResult> GetReviews(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return Json(new { success = false, message = "PlaceId em falta." });

            var result = await _googlePlaces.GetPlaceDetailsAsync(placeId);
            if (result == null)
                return Json(new { success = false, message = "Não foi possível obter dados." });

            return Json(new
            {
                success = true,
                isMock = result.IsMockData,
                rating = result.Rating,
                userRatingsTotal = result.UserRatingsTotal,
                googleMapsUrl = result.GoogleMapsUrl,
                reviews = result.Reviews.Select(r => new
                {
                    author_name = r.AuthorName,
                    rating = r.Rating,
                    text = r.Text,
                    relative_time_description = r.RelativeTimeDescription,
                    profile_photo_url = r.AuthorPhotoUrl
                })
            });
        }

        // ── NEW: GET /Barbershops/PlaceDetails?placeId=... ───────────────────────
        // AJAX endpoint called by the map JS on marker click.
        // Returns full place details for the offcanvas panel.
        [HttpGet]
        public async Task<IActionResult> PlaceDetails(string? placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest(new { success = false, message = "placeId is required." });

            var result = await _googlePlaces.GetFullPlaceDetailsAsync(placeId);

            if (result == null)
                return StatusCode(500, new { success = false, message = "Could not retrieve place details." });

            // Resolve photo proxy URLs server-side so JS never sees photo_reference
            var photosWithUrls = result.Photos.Select((p, i) => new
            {
                index = i,
                proxyUrl = p.GetProxyUrl(800),
                width = p.Width,
                height = p.Height
            }).ToList();

            // Check if this place is favourited by the current user
            var isFavourited = false;
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                isFavourited = await _context.FavouritePlaces
                    .AnyAsync(f => f.UserId == userId && f.PlaceId == placeId);
            }

            return Json(new
            {
                success = true,
                isMock = result.IsMockData,
                placeId = result.PlaceId,
                name = result.Name,
                formattedAddress = result.FormattedAddress,
                formattedPhoneNumber = result.FormattedPhoneNumber,
                website = result.Website,
                rating = result.Rating,
                userRatingsTotal = result.UserRatingsTotal,
                googleMapsUrl = result.GoogleMapsUrl,
                isOpenNow = result.OpeningHours?.IsOpenNow,
                weekdayText = result.OpeningHours?.WeekdayText ?? new List<string>(),
                photos = photosWithUrls,
                reviews = result.Reviews.Select(r => new
                {
                    authorName = r.AuthorName,
                    profilePhotoUrl = r.ProfilePhotoUrl,
                    rating = r.Rating,
                    relativeTimeDescription = r.RelativeTimeDescription,
                    text = r.Text
                }),
                isFavourited = isFavourited
            });
        }

        // ── NEW: GET /Barbershops/PlacePhoto?ref=...&maxWidth=400 ────────────────
        // Photo proxy: fetches the Google Places photo server-side so the API key
        // never appears in browser requests or JS source.
        [HttpGet]
        public async Task<IActionResult> PlacePhoto(string? @ref, int maxWidth = 800)
        {
            if (string.IsNullOrWhiteSpace(@ref))
                return BadRequest();

            var apiKey = _config["Google:PlacesApiKey"] ?? _config["Google:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return NotFound();

            var url = $"https://maps.googleapis.com/maps/api/place/photo" +
                      $"?maxwidth={maxWidth}" +
                      $"&photo_reference={Uri.EscapeDataString(@ref)}" +
                      $"&key={apiKey}";

            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                // Google Places photo API redirects to the actual image — follow it
                var response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return NotFound();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return File(bytes, contentType);
            }
            catch (Exception ex)
            {
                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<BarbershopsController>>();
                logger.LogWarning(ex, "Failed to proxy photo reference {Ref}.", @ref);
                return NotFound();
            }
        }

        // ── NEW: POST /Barbershops/SaveFavourite ─────────────────────────────────
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFavourite([FromBody] SaveFavouriteRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlaceId))
                return BadRequest(new { success = false, message = "Invalid request." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            // Idempotent — do nothing if already favourited
            var exists = await _context.FavouritePlaces
                .AnyAsync(f => f.UserId == userId && f.PlaceId == request.PlaceId);

            if (!exists)
            {
                _context.FavouritePlaces.Add(new FavouritePlace
                {
                    UserId = userId,
                    PlaceId = request.PlaceId,
                    PlaceName = request.PlaceName ?? "Barbearia",
                    PlaceAddress = request.PlaceAddress,
                    SavedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, isFavourited = true });
        }

        // ── NEW: POST /Barbershops/RemoveFavourite ───────────────────────────────
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFavourite([FromBody] SaveFavouriteRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlaceId))
                return BadRequest(new { success = false, message = "Invalid request." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var favourite = await _context.FavouritePlaces
                .FirstOrDefaultAsync(f => f.UserId == userId && f.PlaceId == request.PlaceId);

            if (favourite != null)
            {
                _context.FavouritePlaces.Remove(favourite);
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, isFavourited = false });
        }

        // ── Haversine Distance Utility ─────────────────────────────────────────
        private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);
    }

    /// <summary>
    /// Request body for SaveFavourite / RemoveFavourite AJAX calls.
    /// </summary>
    public class SaveFavouriteRequest
    {
        public string? PlaceId { get; set; }
        public string? PlaceName { get; set; }
        public string? PlaceAddress { get; set; }
    }
}