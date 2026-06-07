using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Models.GooglePlaces;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services
{
    /// <summary>
    /// Production implementation of <see cref="IGooglePlacesService"/>.
    /// 
    /// Caching layers for <see cref="GetPlaceDetailsAsync"/> (in order of lookup):
    ///   1. IMemoryCache — 1-hour TTL in-process cache (zero latency)
    ///   2. CachedGoogleReview DB table — 24-hour TTL persistent cache (survives restarts)
    ///   3. Google Places Details API — live HTTP call (costs an API quota unit)
    ///   4. Mock data — returned when no API key is configured or the API call fails
    ///
    /// <see cref="GetFullPlaceDetailsAsync"/> is NOT cached in DB (photos/hours change often).
    /// It uses a short in-memory cache (5 minutes) and falls back to rich mock data.
    /// </summary>
    public class GooglePlacesService : IGooglePlacesService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _memoryCache;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<GooglePlacesService> _logger;

        private const string CacheKeyPrefix = "google_places_";
        private const string FullDetailsCacheKeyPrefix = "google_full_";
        private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan FullDetailsCacheTtl = TimeSpan.FromMinutes(5);

        public GooglePlacesService(
            HttpClient httpClient,
            IMemoryCache memoryCache,
            ApplicationDbContext context,
            IConfiguration config,
            ILogger<GooglePlacesService> logger)
        {
            _httpClient = httpClient;
            _memoryCache = memoryCache;
            _context = context;
            _config = config;
            _logger = logger;
        }

        // ─── GetPlaceDetailsAsync (existing — reviews + rating only) ──────────────

        /// <inheritdoc />
        public async Task<GooglePlacesResult?> GetPlaceDetailsAsync(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return null;

            var cacheKey = CacheKeyPrefix + placeId;

            // ── Layer 1: Memory cache ──────────────────────────────────────────
            if (_memoryCache.TryGetValue(cacheKey, out GooglePlacesResult? memResult) && memResult != null)
            {
                memResult.FromMemoryCache = true;
                return memResult;
            }

            // ── Layer 2: DB cache ──────────────────────────────────────────────
            var dbEntry = await _context.CachedGoogleReviews
                .FirstOrDefaultAsync(c => c.PlaceId == placeId);

            if (dbEntry != null && !dbEntry.IsExpired)
            {
                var dbResult = DeserializeDbEntry(dbEntry);
                dbResult.FromDbCache = true;
                _memoryCache.Set(cacheKey, dbResult, MemoryCacheTtl);
                return dbResult;
            }

            // ── Layer 3: Live Google Places API ────────────────────────────────
            var apiKey = _config["Google:PlacesApiKey"] ?? _config["Google:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    var url = $"https://maps.googleapis.com/maps/api/place/details/json" +
                              $"?place_id={Uri.EscapeDataString(placeId)}" +
                              $"&fields=rating,user_ratings_total,url,reviews" +
                              $"&language=pt" +
                              $"&key={apiKey}";

                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var liveResult = ParseApiResponse(json);

                        if (liveResult != null)
                        {
                            await PersistToCacheAsync(placeId, liveResult, json, dbEntry);
                            _memoryCache.Set(cacheKey, liveResult, MemoryCacheTtl);
                            return liveResult;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Google Places API call failed for placeId {PlaceId}. Falling back to mock data.", placeId);
                }
            }

            // ── Layer 4: Mock / demo data ──────────────────────────────────────
            var mockResult = BuildMockResult(placeId);
            _memoryCache.Set(cacheKey, mockResult, MemoryCacheTtl);
            return mockResult;
        }

        // ─── GetFullPlaceDetailsAsync (new — full panel data) ─────────────────────

        /// <inheritdoc />
        public async Task<PlaceDetailsResult?> GetFullPlaceDetailsAsync(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return null;

            var cacheKey = FullDetailsCacheKeyPrefix + placeId;

            // Short in-memory cache (5 min) — photos/hours don't change often during a session
            if (_memoryCache.TryGetValue(cacheKey, out PlaceDetailsResult? cached) && cached != null)
                return cached;

            var apiKey = _config["Google:PlacesApiKey"] ?? _config["Google:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                              $"?place_id={Uri.EscapeDataString(placeId)}" +
                              "&fields=place_id,name,rating,user_ratings_total,formatted_phone_number," +
                              "formatted_address,opening_hours,photos,website,reviews,geometry,url" +
                              "&language=pt" +
                              $"&key={apiKey}";

                    _httpClient.Timeout = TimeSpan.FromSeconds(10);
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = ParseFullApiResponse(placeId, json);

                        if (result != null)
                        {
                            _memoryCache.Set(cacheKey, result, FullDetailsCacheTtl);
                            return result;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Google Places full details API returned {Status} for placeId {PlaceId}.",
                            response.StatusCode, placeId);
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "HTTP error fetching full place details for {PlaceId}. Using mock.", placeId);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "Timeout fetching full place details for {PlaceId}. Using mock.", placeId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error fetching full place details for {PlaceId}. Using mock.", placeId);
                }
            }

            // Fallback: rich mock data
            var mock = BuildFullMockResult(placeId);
            _memoryCache.Set(cacheKey, mock, FullDetailsCacheTtl);
            return mock;
        }

        // ─── Private Helpers — GetPlaceDetailsAsync ───────────────────────────────

        private static GooglePlacesResult DeserializeDbEntry(CachedGoogleReview entry)
        {
            var reviews = new List<GoogleReviewItem>();
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                reviews = JsonSerializer.Deserialize<List<GoogleReviewItem>>(entry.ReviewsJson, options)
                          ?? new List<GoogleReviewItem>();
            }
            catch { /* corrupt cache — return empty list */ }

            return new GooglePlacesResult
            {
                Rating = entry.GoogleRating,
                UserRatingsTotal = entry.UserRatingsTotal,
                GoogleMapsUrl = entry.GoogleMapsUrl,
                Reviews = reviews
            };
        }

        private static GooglePlacesResult? ParseApiResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("result", out var result))
                    return null;

                var reviews = new List<GoogleReviewItem>();
                if (result.TryGetProperty("reviews", out var reviewsEl))
                {
                    foreach (var rv in reviewsEl.EnumerateArray())
                    {
                        reviews.Add(new GoogleReviewItem
                        {
                            AuthorName = rv.TryGetProperty("author_name", out var an) ? an.GetString() ?? "Anónimo" : "Anónimo",
                            AuthorPhotoUrl = rv.TryGetProperty("profile_photo_url", out var ph) ? ph.GetString() : null,
                            Rating = rv.TryGetProperty("rating", out var rt) ? rt.GetInt32() : 0,
                            Text = rv.TryGetProperty("text", out var tx) ? tx.GetString() : null,
                            RelativeTimeDescription = rv.TryGetProperty("relative_time_description", out var rtd) ? rtd.GetString() : null,
                            Time = rv.TryGetProperty("time", out var t) ? t.GetInt64() : null
                        });
                    }
                }

                return new GooglePlacesResult
                {
                    Rating = result.TryGetProperty("rating", out var rating) ? rating.GetDouble() : null,
                    UserRatingsTotal = result.TryGetProperty("user_ratings_total", out var urt) ? urt.GetInt32() : null,
                    GoogleMapsUrl = result.TryGetProperty("url", out var url) ? url.GetString() : null,
                    Reviews = reviews
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task PersistToCacheAsync(string placeId, GooglePlacesResult result, string rawJson, CachedGoogleReview? existing)
        {
            try
            {
                var reviewsJson = JsonSerializer.Serialize(result.Reviews);

                if (existing != null)
                {
                    existing.ReviewsJson = reviewsJson;
                    existing.GoogleRating = result.Rating;
                    existing.UserRatingsTotal = result.UserRatingsTotal;
                    existing.GoogleMapsUrl = result.GoogleMapsUrl;
                    existing.FetchedAt = DateTime.UtcNow;
                    _context.CachedGoogleReviews.Update(existing);
                }
                else
                {
                    _context.CachedGoogleReviews.Add(new CachedGoogleReview
                    {
                        PlaceId = placeId,
                        ReviewsJson = reviewsJson,
                        GoogleRating = result.Rating,
                        UserRatingsTotal = result.UserRatingsTotal,
                        GoogleMapsUrl = result.GoogleMapsUrl,
                        FetchedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist Google Places cache for placeId {PlaceId}.", placeId);
            }
        }

        private static GooglePlacesResult BuildMockResult(string placeId)
        {
            var seed = placeId.GetHashCode();
            var rng = new Random(Math.Abs(seed));

            var names = new[] { "João M.", "Ana S.", "Ricardo P.", "Marta C.", "Carlos F.", "Sofia L." };
            var comments = new[]
            {
                "Excelente atendimento, muito profissional! Recomendo vivamente.",
                "Ótimo serviço, fiquei muito satisfeito com o resultado. Voltarei certamente.",
                "Bom corte, preços justos e ambiente agradável.",
                "Profissionais competentes e simpáticos. O melhor da área!",
                "Serviço rápido e de qualidade. Fica mesmo perto de casa.",
                "Muito bom! Atendimento de excelência e o espaço está impecável."
            };
            var times = new[] { "há 2 dias", "há 1 semana", "há 2 semanas", "há 1 mês", "há 3 meses", "há 6 meses" };

            var count = rng.Next(3, 6);
            var reviews = new List<GoogleReviewItem>();
            for (int i = 0; i < count; i++)
            {
                reviews.Add(new GoogleReviewItem
                {
                    AuthorName = names[rng.Next(names.Length)],
                    Rating = rng.Next(4, 6),
                    Text = comments[rng.Next(comments.Length)],
                    RelativeTimeDescription = times[i % times.Length]
                });
            }

            return new GooglePlacesResult
            {
                Rating = Math.Round(3.8 + rng.NextDouble() * 1.2, 1),
                UserRatingsTotal = rng.Next(12, 300),
                GoogleMapsUrl = $"https://maps.google.com/?q={Uri.EscapeDataString(placeId)}",
                Reviews = reviews,
                IsMockData = true
            };
        }

        // ─── Private Helpers — GetFullPlaceDetailsAsync ───────────────────────────

        private static PlaceDetailsResult? ParseFullApiResponse(string placeId, string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("result", out var r))
                    return null;

                // Opening hours
                PlaceOpeningHours? openingHours = null;
                if (r.TryGetProperty("opening_hours", out var oh))
                {
                    openingHours = new PlaceOpeningHours
                    {
                        IsOpenNow = oh.TryGetProperty("open_now", out var on) ? on.GetBoolean() : null
                    };
                    if (oh.TryGetProperty("weekday_text", out var wt))
                    {
                        foreach (var day in wt.EnumerateArray())
                        {
                            var dayStr = day.GetString();
                            if (dayStr != null) openingHours.WeekdayText.Add(dayStr);
                        }
                    }
                }

                // Photos (up to 5)
                var photos = new List<PlacePhoto>();
                if (r.TryGetProperty("photos", out var photosEl))
                {
                    foreach (var p in photosEl.EnumerateArray().Take(5))
                    {
                        if (p.TryGetProperty("photo_reference", out var pr))
                        {
                            photos.Add(new PlacePhoto
                            {
                                PhotoReference = pr.GetString() ?? string.Empty,
                                Width = p.TryGetProperty("width", out var w) ? w.GetInt32() : 800,
                                Height = p.TryGetProperty("height", out var h) ? h.GetInt32() : 600
                            });
                        }
                    }
                }

                // Reviews (up to 5)
                var reviews = new List<PlaceReview>();
                if (r.TryGetProperty("reviews", out var revEl))
                {
                    foreach (var rv in revEl.EnumerateArray().Take(5))
                    {
                        reviews.Add(new PlaceReview
                        {
                            AuthorName = rv.TryGetProperty("author_name", out var an) ? an.GetString() ?? "Anónimo" : "Anónimo",
                            ProfilePhotoUrl = rv.TryGetProperty("profile_photo_url", out var pp) ? pp.GetString() : null,
                            Rating = rv.TryGetProperty("rating", out var rat) ? rat.GetInt32() : 0,
                            RelativeTimeDescription = rv.TryGetProperty("relative_time_description", out var rtd) ? rtd.GetString() : null,
                            Text = rv.TryGetProperty("text", out var tx) ? tx.GetString() : null,
                            Time = rv.TryGetProperty("time", out var t) ? t.GetInt64() : null
                        });
                    }
                }

                // Geometry
                double? lat = null, lng = null;
                if (r.TryGetProperty("geometry", out var geo) &&
                    geo.TryGetProperty("location", out var loc))
                {
                    lat = loc.TryGetProperty("lat", out var latEl) ? latEl.GetDouble() : null;
                    lng = loc.TryGetProperty("lng", out var lngEl) ? lngEl.GetDouble() : null;
                }

                return new PlaceDetailsResult
                {
                    PlaceId = placeId,
                    Name = r.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    FormattedAddress = r.TryGetProperty("formatted_address", out var addr) ? addr.GetString() : null,
                    FormattedPhoneNumber = r.TryGetProperty("formatted_phone_number", out var phone) ? phone.GetString() : null,
                    Website = r.TryGetProperty("website", out var web) ? web.GetString() : null,
                    Rating = r.TryGetProperty("rating", out var rating) ? rating.GetDouble() : null,
                    UserRatingsTotal = r.TryGetProperty("user_ratings_total", out var urt) ? urt.GetInt32() : null,
                    GoogleMapsUrl = r.TryGetProperty("url", out var url) ? url.GetString() : null,
                    OpeningHours = openingHours,
                    Photos = photos,
                    Reviews = reviews,
                    Lat = lat,
                    Lng = lng
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static PlaceDetailsResult BuildFullMockResult(string placeId)
        {
            var seed = Math.Abs(placeId.GetHashCode());
            var rng = new Random(seed);

            var names = new[] { "Barbearia Central", "Salão Elegante", "UrbanCuts Studio", "Barbershop Deluxe", "Corte & Arte" };
            var addresses = new[] { "Rua Augusta 120, Lisboa", "Av. da Liberdade 55, Lisboa", "Rua do Carmo 8, Lisboa", "Praça do Comércio 2, Lisboa" };
            var phones = new[] { "+351 21 000 1111", "+351 21 000 2222", "+351 21 000 3333" };
            var websites = new[] { "https://www.barbershop.pt", "https://www.salaobarbeiro.pt", null };
            var reviewers = new[] { "João M.", "Ana S.", "Ricardo P.", "Marta C.", "Carlos F.", "Sofia L.", "Pedro A.", "Inês R." };
            var comments = new[]
            {
                "Excelente atendimento! O barbeiro sabe bem o que está a fazer. Saí com um corte perfeito.",
                "Ótimo ambiente e profissionais fantásticos. Recomendo a toda a gente.",
                "Preços muito razoáveis para a qualidade do serviço. Voltarei com certeza!",
                "A melhor barbearia da zona. Atendimento de cinco estrelas sem dúvida.",
                "Fiquei muito satisfeito. Marcação fácil e pontualidade total.",
                "Serviço impecável. O espaço é muito agradável e limpo."
            };
            var times = new[] { "há 3 dias", "há 1 semana", "há 2 semanas", "há 1 mês", "há 2 meses" };
            var weekdays = new[]
            {
                "Segunda-feira: 09:00 – 20:00",
                "Terça-feira: 09:00 – 20:00",
                "Quarta-feira: 09:00 – 20:00",
                "Quinta-feira: 09:00 – 20:00",
                "Sexta-feira: 09:00 – 20:00",
                "Sábado: 09:00 – 18:00",
                "Domingo: Fechado"
            };

            var reviewCount = rng.Next(3, 6);
            var reviews = new List<PlaceReview>();
            for (int i = 0; i < reviewCount; i++)
            {
                reviews.Add(new PlaceReview
                {
                    AuthorName = reviewers[rng.Next(reviewers.Length)],
                    Rating = rng.Next(4, 6),
                    Text = comments[rng.Next(comments.Length)],
                    RelativeTimeDescription = times[i % times.Length],
                    ProfilePhotoUrl = null
                });
            }

            return new PlaceDetailsResult
            {
                PlaceId = placeId,
                Name = names[seed % names.Length],
                FormattedAddress = addresses[seed % addresses.Length],
                FormattedPhoneNumber = phones[seed % phones.Length],
                Website = websites[seed % websites.Length],
                Rating = Math.Round(3.8 + rng.NextDouble() * 1.2, 1),
                UserRatingsTotal = rng.Next(20, 350),
                GoogleMapsUrl = $"https://maps.google.com/?q={Uri.EscapeDataString(placeId)}",
                OpeningHours = new PlaceOpeningHours
                {
                    IsOpenNow = rng.Next(2) == 0,
                    WeekdayText = weekdays.ToList()
                },
                Photos = new List<PlacePhoto>(), // No mock photos — panel handles empty gracefully
                Reviews = reviews,
                IsMockData = true
            };
        }
    }
}
