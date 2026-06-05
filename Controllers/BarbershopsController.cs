using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class BarbershopsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;

        public BarbershopsController(ApplicationDbContext context, IConfiguration config, IMemoryCache cache)
        {
            _context = context;
            _config = config;
            _cache = cache;
        }

        // GET: Barbershops
        public async Task<IActionResult> Index(string searchString, string sortOrder, string category)
        {
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["RatingSortParm"] = sortOrder == "Rating" ? "rating_desc" : "Rating";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentCategory"] = category;

            var barbershops = from b in _context.Barbershops
                            .Include(b => b.Reviews)
                            select b;

            if (!String.IsNullOrEmpty(searchString))
            {
                barbershops = barbershops.Where(b => b.Name.Contains(searchString)
                                       || b.Address.Contains(searchString));
            }

            if (!String.IsNullOrEmpty(category))
            {
                if (Enum.TryParse<BarbershopCategory>(category, out var cat))
                {
                    barbershops = barbershops.Where(b => b.Category == cat);
                }
            }

            switch (sortOrder)
            {
                case "name_desc":
                    barbershops = barbershops.OrderByDescending(b => b.Name);
                    break;
                case "Rating":
                    barbershops = barbershops.OrderBy(b => b.AverageRating);
                    break;
                case "rating_desc":
                    barbershops = barbershops.OrderByDescending(b => b.AverageRating);
                    break;
                default:
                    barbershops = barbershops.OrderBy(b => b.Name);
                    break;
            }

            return View(await barbershops.Where(b => b.IsActive).ToListAsync());
        }

        // GET: Barbershops/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var barbershop = await _context.Barbershops
                .Include(b => b.Reviews)
                    .ThenInclude(r => r.User)
                .Include(b => b.Services)
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (barbershop == null)
            {
                return NotFound();
            }

            return View(barbershop);
        }

        // GET: Barbershops/Map
        public IActionResult Map()
        {
            // Provide Google Maps API key from configuration to the view (kept out of source files)
            ViewData["GoogleApiKey"] = _config["Google:ApiKey"] ?? string.Empty;
            return View();
        }

        // GET: Barbershops/GetMapData
        // Supports optional query params: lat, lng, radiusKm (defaults to 10), minRating, categories (comma separated: Barbershop,HairSalon,Unisex)
        [HttpGet]
        public async Task<JsonResult> GetMapData(double? lat, double? lng, double? radiusKm, double? minRating, string? categories, string? genders, bool? mobileOnly)
        {
            var cats = new List<string>();
            if (!string.IsNullOrEmpty(categories))
            {
                cats = categories.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }

            var genderFilters = new List<string>();
            if (!string.IsNullOrEmpty(genders))
            {
                genderFilters = genders.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }

            // Load barbershops with services for filtering capabilities
            var bars = await _context.Barbershops
                .Include(b => b.Services)
                .Where(b => b.IsActive)
                .ToListAsync();

            // Filter by category if provided
            if (cats.Any())
            {
                bars = bars.Where(b => cats.Contains(b.Category.ToString())).ToList();
            }

            var results = new List<object>();
            foreach (var b in bars)
            {
                // mobile availability and gender match logic
                var hasMobile = b.Services != null && b.Services.Any(s => s.IsAvailable && s.IsMobile);
                var genderMatch = true;
                if (genderFilters.Any())
                {
                    genderMatch = b.Services != null && b.Services.Any(s => s.IsAvailable && (genderFilters.Contains(s.TargetGender.ToString()) || s.TargetGender == Models.TargetGender.Unisex));
                }

                if (!genderMatch) continue;
                if (mobileOnly == true && !hasMobile) continue;

                results.Add(new {
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

            // If lat/lng provided, compute distances and potential travel fee

            if (lat.HasValue && lng.HasValue)
            {
                var radius = radiusKm ?? 10.0;
                var enriched = new List<object>();
                foreach (var r in results)
                {
                    // dynamic access
                    var dlat = (double)r.GetType().GetProperty("lat")!.GetValue(r)!;
                    var dlng = (double)r.GetType().GetProperty("lng")!.GetValue(r)!;
                    var dist = HaversineDistance(lat.Value, lng.Value, dlat, dlng);
                    if (dist <= radius)
                    {
                        // estimate travel fee: base 5 + 0.75 per km if hasMobile
                        var hasMobileFlag = (bool)r.GetType().GetProperty("hasMobile")!.GetValue(r)!;
                        decimal? fee = null;
                        if (hasMobileFlag)
                        {
                            fee = Math.Round(5.0m + (decimal)dist * 0.75m, 2);
                        }

                        enriched.Add(new {
                            id = r.GetType().GetProperty("id")!.GetValue(r),
                            name = r.GetType().GetProperty("name")!.GetValue(r),
                            lat = dlat,
                            lng = dlng,
                            address = r.GetType().GetProperty("address")!.GetValue(r),
                            category = r.GetType().GetProperty("category")!.GetValue(r),
                            rating = r.GetType().GetProperty("rating")!.GetValue(r),
                            image = r.GetType().GetProperty("image")?.GetValue(r),
                            phone = r.GetType().GetProperty("phone")?.GetValue(r),
                            hasMobile = hasMobileFlag,
                            distanceKm = Math.Round(dist, 2),
                            estimatedTravelFee = fee
                        });
                    }
                }

                return Json(enriched.OrderByDescending(x => (double)x.GetType().GetProperty("rating")!.GetValue(x)).ToList());
            }

            if (minRating.HasValue)
            {
                var min = minRating.Value;
                var filtered = results.Where(r => (double)r.GetType().GetProperty("rating")!.GetValue(r)! >= min).OrderByDescending(r => (double)r.GetType().GetProperty("rating")!.GetValue(r)! ).ToList();
                return Json(filtered);
            }

            return Json(results.OrderByDescending(r => (double)r.GetType().GetProperty("rating")!.GetValue(r)!).Take(50));
        }

        private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371.0; // km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat/2) * Math.Sin(dLat/2) + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon/2) * Math.Sin(dLon/2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
            return R * c;
        }

        private static double ToRadians(double deg) => deg * (Math.PI / 180.0);

        // GET: Barbershops/GetReviews?placeId=...
        [HttpGet]
        public async Task<JsonResult> GetReviews(string placeId)
        {
            if (string.IsNullOrEmpty(placeId)) return Json(new { success = false, message = "no placeId" });

            var cacheKey = "gm_reviews_" + placeId;
            if (_cache.TryGetValue(cacheKey, out object cached))
            {
                return Json(new { success = true, reviews = cached });
            }

            var apiKey = _config["Google:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                // Fallback to mock Google reviews if no API key is configured
                var mockReviews = GenerateMockGoogleReviews(placeId);
                return Json(new { success = true, reviews = mockReviews, isMock = true });
            }

            try
            {
                using var http = new System.Net.Http.HttpClient();
                var url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={placeId}&fields=name,rating,reviews&key={apiKey}";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    return Json(new { success = true, reviews = GenerateMockGoogleReviews(placeId), isMock = true });
                }
                var json = await resp.Content.ReadAsStringAsync();
                _cache.Set(cacheKey, json, TimeSpan.FromHours(1));
                return Json(new { success = true, reviews = json });
            }
            catch (Exception)
            {
                return Json(new { success = true, reviews = GenerateMockGoogleReviews(placeId), isMock = true });
            }
        }

        private string GenerateMockGoogleReviews(string placeId)
        {
            var shop = _context.Barbershops.FirstOrDefault(b => b.PlaceId == placeId);
            var shopName = shop?.Name ?? "Estabelecimento";
            var rating = shop?.AverageRating ?? 4.5;
            
            var reviews = new[]
            {
                new {
                    author_name = "Carlos Martins",
                    rating = 5,
                    relative_time_description = "há uma semana",
                    text = $"Excelente atendimento no {shopName}! O corte ficou exatamente como pedi e o ambiente é fantástico. Recomendo muito!"
                },
                new {
                    author_name = "Ana Rodrigues",
                    rating = 4,
                    relative_time_description = "há 2 semanas",
                    text = $"Muito profissional. Fui muito bem recebida e o serviço foi rápido e de qualidade. A repetir, sem dúvida."
                },
                new {
                    author_name = "Pedro Silva",
                    rating = 5,
                    relative_time_description = "há um mês",
                    text = $"Melhor sítio da região para cuidar do cabelo e barba. O staff é super simpático e atencioso. 5 estrelas!"
                }
            };
            
            var result = new {
                result = new {
                    name = shopName,
                    rating = rating,
                    reviews = reviews
                }
            };
            
            return System.Text.Json.JsonSerializer.Serialize(result);
        }


    }
}
