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
        private readonly GoogleMapsOptions _options;
        private readonly ILogger<GooglePlacesService> _logger;

        private const string CacheKeyPrefix = "google_places_";
        private const string FullDetailsCacheKeyPrefix = "google_full_";
        private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan FullDetailsCacheTtl = TimeSpan.FromMinutes(5);

        public GooglePlacesService(
            HttpClient httpClient,
            IMemoryCache memoryCache,
            ApplicationDbContext context,
            Microsoft.Extensions.Options.IOptions<GoogleMapsOptions> options,
            ILogger<GooglePlacesService> logger)
        {
            _httpClient = httpClient;
            _memoryCache = memoryCache;
            _context = context;
            _options = options.Value;
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
            var apiKey = !string.IsNullOrWhiteSpace(_options.PlacesApiKey) ? _options.PlacesApiKey : _options.ApiKey;
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

            // Live Google Places API failure/no data fallback
            return null;
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

            var apiKey = !string.IsNullOrWhiteSpace(_options.PlacesApiKey) ? _options.PlacesApiKey : _options.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                              $"?place_id={Uri.EscapeDataString(placeId)}" +
                              "&fields=place_id,name,rating,user_ratings_total,formatted_phone_number,international_phone_number," +
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

            // Fallback: no mock data permitted. Return null on failure.
            return null;
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
                    InternationalPhoneNumber = r.TryGetProperty("international_phone_number", out var intPhone) ? intPhone.GetString() : null,
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



        // ─── VerifyPlaceStatusAsync ────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<PlaceVerificationResult> VerifyPlaceStatusAsync(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return new PlaceVerificationResult
                {
                    Status       = Models.OperationalStatus.Unverified,
                    ErrorMessage = "placeId was empty."
                };

            var apiKey = !string.IsNullOrWhiteSpace(_options.PlacesApiKey) ? _options.PlacesApiKey : _options.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                return new PlaceVerificationResult
                {
                    Status       = Models.OperationalStatus.Unverified,
                    ErrorMessage = "No Google Places API key configured."
                };

            try
            {
                // Request only the business_status field to minimise API quota usage.
                var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                          $"?place_id={Uri.EscapeDataString(placeId)}" +
                          "&fields=business_status" +
                          $"&key={apiKey}";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var response = await _httpClient.GetAsync(url, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "[GooglePlacesService] VerifyPlaceStatus returned HTTP {Status} for {PlaceId}.",
                        response.StatusCode, placeId);
                    return new PlaceVerificationResult
                    {
                        Status       = Models.OperationalStatus.Unverified,
                        ErrorMessage = $"HTTP {(int)response.StatusCode}"
                    };
                }

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Top-level status check: Google returns "ZERO_RESULTS" or "NOT_FOUND" for invalid IDs.
                if (root.TryGetProperty("status", out var topStatus))
                {
                    var googleStatus = topStatus.GetString();
                    if (googleStatus is "ZERO_RESULTS" or "NOT_FOUND" or "INVALID_REQUEST")
                    {
                        _logger.LogWarning(
                            "[GooglePlacesService] VerifyPlaceStatus API status={GStatus} for {PlaceId}.",
                            googleStatus, placeId);
                        return new PlaceVerificationResult
                        {
                            Status          = Models.OperationalStatus.Unverified,
                            RawBusinessStatus = googleStatus,
                            ErrorMessage    = $"API returned {googleStatus}"
                        };
                    }
                }

                string? businessStatus = null;
                if (root.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("business_status", out var bsEl))
                {
                    businessStatus = bsEl.GetString();
                }

                var mappedStatus = MapBusinessStatus(businessStatus);

                _logger.LogInformation(
                    "[GooglePlacesService] VerifyPlaceStatus: {PlaceId} → {BusinessStatus} → {MappedStatus}.",
                    placeId, businessStatus ?? "null", mappedStatus);

                return new PlaceVerificationResult
                {
                    Status            = mappedStatus,
                    RawBusinessStatus = businessStatus,
                    IsLive            = true
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[GooglePlacesService] VerifyPlaceStatus timed out for {PlaceId}.", placeId);
                return new PlaceVerificationResult
                {
                    Status       = Models.OperationalStatus.Unverified,
                    ErrorMessage = "API request timed out."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GooglePlacesService] VerifyPlaceStatus unexpected error for {PlaceId}.", placeId);
                return new PlaceVerificationResult
                {
                    Status       = Models.OperationalStatus.Unverified,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Maps a Google Places API <c>business_status</c> string to our <see cref="Models.OperationalStatus"/> enum.
        /// </summary>
        private static Models.OperationalStatus MapBusinessStatus(string? businessStatus) =>
            businessStatus switch
            {
                "OPERATIONAL"         => Models.OperationalStatus.Active,
                "CLOSED_PERMANENTLY"  => Models.OperationalStatus.PermanentlyClosed,
                "CLOSED_TEMPORARILY"  => Models.OperationalStatus.TemporarilyClosed,
                _                     => Models.OperationalStatus.Unverified
            };

        // ─── SearchNearbyBarbershopsAsync ──────────────────────────────────────────

        /// <inheritdoc />
        public async Task<IEnumerable<Models.ViewModels.BarberShopPlaceViewModel>> SearchNearbyBarbershopsAsync(
            double lat, double lng, int radiusMeters)
        {
            var apiKey = !string.IsNullOrWhiteSpace(_options.PlacesApiKey) ? _options.PlacesApiKey : _options.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogCritical("[GooglePlacesService] SearchNearbyBarbershopsAsync: No API key configured. Cannot fetch places.");
                throw new Exception("No Google Places API key configured.");
            }

            var cacheKey = $"nearby_{lat:F4}_{lng:F4}_{radiusMeters}";
            if (_memoryCache.TryGetValue(cacheKey, out IEnumerable<Models.ViewModels.BarberShopPlaceViewModel>? cached) && cached != null)
                return cached;

            var url = "https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
                      $"?location={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                      $"&radius={radiusMeters}" +
                      "&type=hair_care" +
                      "&language=pt" +
                      $"&key={apiKey}";

            var maskedUrl = url.Replace(apiKey, "HIDDEN_API_KEY");
            _logger.LogInformation("[GooglePlacesService] SearchNearbyBarbershopsAsync Calling Google API. URL: {Url}", maskedUrl);

            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            var response = await _httpClient.GetAsync(url);
            
            _logger.LogInformation("[GooglePlacesService] SearchNearbyBarbershopsAsync HTTP Status Code: {StatusCode}", response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[GooglePlacesService] SearchNearbyBarbershopsAsync Raw JSON Response BEFORE parsing:\n{Json}", json);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogCritical("[GooglePlacesService] SearchNearbyBarbershopsAsync HTTP request failed with status {StatusCode}. Response: {Json}", response.StatusCode, json);
                throw new Exception($"Google API HTTP request failed with status {response.StatusCode}.");
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : "UNKNOWN";

            if (status != "OK" && status != "ZERO_RESULTS")
            {
                var errorMsg = root.TryGetProperty("error_message", out var errEl) ? errEl.GetString() : "No error_message provided";
                _logger.LogCritical("[GooglePlacesService] SearchNearbyBarbershopsAsync Google API returned invalid status {Status}. Error Message: {ErrorMessage}", status, errorMsg);
                throw new Exception($"Google API returned invalid status: {status}. Message: {errorMsg}");
            }

            var results = ParseNearbySearchResponse(json, apiKey).ToList();
            _memoryCache.Set(cacheKey, results, TimeSpan.FromMinutes(10));
            return results;
        }

        // ─── FetchLiveBarbershopsAsync ─────────────────────────────────────────────

        /// <inheritdoc />
        public async Task<IReadOnlyList<Models.ViewModels.BarberShopPlaceViewModel>> FetchLiveBarbershopsAsync(
            double lat, double lng, int radiusInMeters, string? query = null)
        {
            _logger.LogInformation("[GooglePlacesService] FetchLiveBarbershopsAsync called with lat={Lat}, lng={Lng}, radius={Radius}, query={Query}", lat, lng, radiusInMeters, query);

            // Default search fallback if user coordinates are (0,0) or invalid
            if (lat == 0 && lng == 0 || lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                lat = 38.7223; // Lisbon
                lng = -9.1393;
                _logger.LogInformation("[GooglePlacesService] Using fallback location: Lisbon (38.7223, -9.1393)");
            }

            // Expand query to include hairdressers and salons as requested by user
            var searchQuery = string.IsNullOrWhiteSpace(query) || query.Equals("Barbearia", StringComparison.OrdinalIgnoreCase) 
                ? "barbearia, cabeleireiro, salão de beleza" 
                : query;

            // Clamp radius to the API maximum
            radiusInMeters = Math.Clamp(radiusInMeters, 100, 50_000);

            var apiKey = !string.IsNullOrWhiteSpace(_options.PlacesApiKey) ? _options.PlacesApiKey : _options.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogCritical("[GooglePlacesService] FetchLiveBarbershopsAsync: No API key configured. Cannot fetch places.");
                throw new Exception("No Google Places API key configured.");
            }

            var cacheKey = $"live_textsearch_{lat:F3}_{lng:F3}_{radiusInMeters}_{searchQuery}";
            if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<Models.ViewModels.BarberShopPlaceViewModel>? cachedLive) && cachedLive != null)
            {
                _logger.LogInformation("[GooglePlacesService] Returning {Count} results from cache.", cachedLive.Count);
                return cachedLive;
            }

            var latStr = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lngStr = lng.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var initialUrl = "https://maps.googleapis.com/maps/api/place/textsearch/json" +
                      $"?query={Uri.EscapeDataString(searchQuery)}" +
                      $"&location={latStr},{lngStr}" +
                      $"&radius={radiusInMeters}" +
                      "&language=pt" +
                      $"&key={apiKey}";

            _httpClient.Timeout = TimeSpan.FromSeconds(30); // increased timeout for multiple pages
            var results = new List<Models.ViewModels.BarberShopPlaceViewModel>();
            string? nextPageToken = null;

            for (int page = 0; page < 3; page++) // Max 3 pages (up to 60 results)
            {
                var url = initialUrl;
                if (!string.IsNullOrEmpty(nextPageToken))
                {
                    // Delay required by Google API before nextPageToken is valid
                    await Task.Delay(2000);
                    url = $"https://maps.googleapis.com/maps/api/place/textsearch/json?pagetoken={nextPageToken}&key={apiKey}";
                }

                var maskedUrl = url.Replace(apiKey, "HIDDEN_API_KEY");
                _logger.LogInformation("[GooglePlacesService] Calling Google API Page {Page}. URL: {Url}", page + 1, maskedUrl);

                var response = await _httpClient.GetAsync(url);
                _logger.LogInformation("[GooglePlacesService] HTTP Status Code: {StatusCode}", response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync();
                    _logger.LogCritical("[GooglePlacesService] HTTP request failed with status {StatusCode}. Response: {Json}", response.StatusCode, errorJson);
                    if (page == 0) throw new Exception($"Google API HTTP request failed with status {response.StatusCode}.");
                    break;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : "UNKNOWN";

                if (status != "OK" && status != "ZERO_RESULTS")
                {
                    // If INVALID_REQUEST occurs on page 2+, it might mean the token wasn't ready yet.
                    if (status == "INVALID_REQUEST" && page > 0)
                    {
                        _logger.LogWarning("[GooglePlacesService] Token not ready. Breaking loop.");
                        break;
                    }
                    var errorMsg = root.TryGetProperty("error_message", out var errEl) ? errEl.GetString() : "No error_message provided";
                    _logger.LogCritical("[GooglePlacesService] Google API returned invalid status {Status}. Error Message: {ErrorMessage}", status, errorMsg);
                    if (page == 0) throw new Exception($"Google API returned invalid status: {status}. Message: {errorMsg}");
                    break;
                }

                if (status == "OK" && root.TryGetProperty("results", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var placeId = item.TryGetProperty("place_id", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                        if (string.IsNullOrEmpty(placeId) || results.Any(x => x.PlaceId == placeId)) continue;

                        double? itemLat = null, itemLng = null;
                        if (item.TryGetProperty("geometry", out var geo) &&
                            geo.TryGetProperty("location", out var loc))
                        {
                            itemLat = loc.TryGetProperty("lat", out var latEl) ? latEl.GetDouble() : null;
                            itemLng = loc.TryGetProperty("lng", out var lngEl) ? lngEl.GetDouble() : null;
                        }

                        if (itemLat == null || itemLng == null) continue;

                        string? photoRef = null;
                        if (item.TryGetProperty("photos", out var photos) &&
                            photos.GetArrayLength() > 0 &&
                            photos[0].TryGetProperty("photo_reference", out var pr))
                        {
                            photoRef = pr.GetString();
                        }

                        // Read the 'types' array from the Google API response — this is the authoritative
                        // signal for classification. e.g. ["barber"], ["hair_salon", "beauty_salon"], etc.
                        var itemTypes = new List<string>();
                        if (item.TryGetProperty("types", out var typesEl))
                        {
                            foreach (var t in typesEl.EnumerateArray())
                            {
                                var typeStr = t.GetString();
                                if (!string.IsNullOrEmpty(typeStr)) itemTypes.Add(typeStr);
                            }
                        }

                        var itemName = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;

                        results.Add(new Models.ViewModels.BarberShopPlaceViewModel
                        {
                            PlaceId = placeId,
                            Name = itemName,
                            Address = item.TryGetProperty("formatted_address", out var fmtAddr) ? fmtAddr.GetString() :
                                      (item.TryGetProperty("vicinity", out var vic) ? vic.GetString() : null),
                            Rating = item.TryGetProperty("rating", out var rat) ? rat.GetDouble() : null,
                            UserRatingsTotal = item.TryGetProperty("user_ratings_total", out var urt) ? urt.GetInt32() : null,
                            Lat = itemLat.Value,
                            Lng = itemLng.Value,
                            PhotoReference = photoRef,
                            // Set Category authoritatively from types[] — eliminates all downstream heuristics.
                            Category = ClassifyCategoryFromTypes(itemTypes, itemName)
                        });
                    }
                }

                if (root.TryGetProperty("next_page_token", out var tokenEl))
                {
                    nextPageToken = tokenEl.GetString();
                }
                else
                {
                    break; // No more pages available
                }
            }

            var resultList = results.OrderByDescending(p => p.Rating ?? 0).ToList();
            _memoryCache.Set(cacheKey, resultList, TimeSpan.FromMinutes(10));
            
            _logger.LogInformation("[GooglePlacesService] Successfully mapped {Count} places across pages.", resultList.Count);

            return resultList;
        }

        /// <summary>
        /// Classifies a Google Places result into one of our three categories using the authoritative
        /// <c>types[]</c> array returned by the API. Falls back to name-keyword matching only when the
        /// <c>types</c> array is absent or contains only generic types (e.g. "establishment", "point_of_interest").
        /// </summary>
        /// <param name="types">The list of type strings from the Google Places <c>types</c> array.</param>
        /// <param name="name">The establishment name — used only as a secondary fallback signal.</param>
        /// <returns>
        ///   <c>"Barbershop"</c> | <c>"HairSalon"</c> | <c>"Unisex"</c>
        /// </returns>
        private static string ClassifyCategoryFromTypes(IReadOnlyList<string> types, string name)
        {
            bool hasBarber    = types.Contains("barber");
            bool hasHairSalon = types.Contains("hair_salon") || types.Contains("beauty_salon");

            // Authoritative API-level classification — no name guessing needed.
            if (hasBarber && hasHairSalon) return "Unisex";
            if (hasBarber)                return "Barbershop";
            if (hasHairSalon)             return "HairSalon";

            // Secondary fallback: use the business name only when types[] is inconclusive.
            // This handles edge cases where Google returns only generic types like
            // ["establishment", "point_of_interest"] for some results.
            var lower = name.ToLowerInvariant();

            if (lower.Contains("barbearia") || lower.Contains("barber") || lower.Contains("navalha"))
                return "Barbershop";

            if (lower.Contains("cabeleireiro") || lower.Contains("salão") || lower.Contains("salon") ||
                lower.Contains("beauty")       || lower.Contains("spa")   || lower.Contains("nails") ||
                lower.Contains("unhas")        || lower.Contains("xb")    || lower.Contains("feminino") ||
                lower.Contains("vocêviva")     || lower.Contains("estética"))
                return "HairSalon";

            if (lower.Contains("unisex") || lower.Contains("hair") || lower.Contains("studio"))
                return "Unisex";

            // Final default: treat as a barbershop (the most common result from a barber-biased query).
            return "Barbershop";
        }

        private static IEnumerable<Models.ViewModels.BarberShopPlaceViewModel> ParseNearbySearchResponse(string json, string apiKey)
        {
            var results = new List<Models.ViewModels.BarberShopPlaceViewModel>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var items))
                    return results;

                foreach (var item in items.EnumerateArray().Take(50))
                {
                    var placeId = item.TryGetProperty("place_id", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(placeId)) continue;

                    double? lat = null, lng = null;
                    if (item.TryGetProperty("geometry", out var geo) &&
                        geo.TryGetProperty("location", out var loc))
                    {
                        lat = loc.TryGetProperty("lat", out var latEl) ? latEl.GetDouble() : null;
                        lng = loc.TryGetProperty("lng", out var lngEl) ? lngEl.GetDouble() : null;
                    }

                    if (lat == null || lng == null) continue;

                    string? photoRef = null;
                    if (item.TryGetProperty("photos", out var photos) &&
                        photos.GetArrayLength() > 0 &&
                        photos[0].TryGetProperty("photo_reference", out var pr))
                    {
                        photoRef = pr.GetString();
                    }

                    results.Add(new Models.ViewModels.BarberShopPlaceViewModel
                    {
                        PlaceId = placeId,
                        Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        Address = item.TryGetProperty("vicinity", out var vic) ? vic.GetString() : null,
                        Rating = item.TryGetProperty("rating", out var rat) ? rat.GetDouble() : null,
                        UserRatingsTotal = item.TryGetProperty("user_ratings_total", out var urt) ? urt.GetInt32() : null,
                        Lat = lat.Value,
                        Lng = lng.Value,
                        PhotoReference = photoRef
                    });
                }
            }
            catch (Exception)
            {
                // Malformed response — return whatever was parsed so far
            }

            return results;
        }
    }
}
