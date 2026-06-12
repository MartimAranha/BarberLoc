using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

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
            var query = _context.Barbershops
                .Include(b => b.Services)
                .Where(b => b.IsActive)
                .AsQueryable();

            // ── Full-text search ───────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(vm.SearchQuery))
            {
                var q = vm.SearchQuery.Trim().ToLower();
                query = query.Where(b =>
                    b.Name.ToLower().Contains(q) ||
                    (b.Description != null && b.Description.ToLower().Contains(q)) ||
                    b.Address.ToLower().Contains(q));
            }

            // ── Category filter ────────────────────────────────────────────────
            if (vm.SelectedCategories.Any())
            {
                var cats = vm.SelectedCategories
                    .Select(c => Enum.TryParse<BarbershopCategory>(c, out var r) ? r : (BarbershopCategory?)null)
                    .Where(c => c.HasValue)
                    .Select(c => c!.Value)
                    .ToList();

                if (cats.Any())
                    query = query.Where(b => cats.Contains(b.Category));
            }

            // ── Minimum rating filter ──────────────────────────────────────────
            if (vm.MinRating.HasValue)
                query = query.Where(b => b.AverageRating >= vm.MinRating.Value);

            // ── Load results with services for further in-memory filtering ─────
            var allResults = await query.ToListAsync();

            // ── Gender filter (in-memory, requires Services navigation) ─────────
            if (vm.SelectedGenders.Any())
            {
                var genders = vm.SelectedGenders
                    .Select(g => Enum.TryParse<TargetGender>(g, out var r) ? r : (TargetGender?)null)
                    .Where(g => g.HasValue)
                    .Select(g => g!.Value)
                    .ToList();

                allResults = allResults
                    .Where(b => b.Services.Any(s =>
                        s.IsAvailable &&
                        (genders.Contains(s.TargetGender) || s.TargetGender == TargetGender.Unisex)))
                    .ToList();
            }

            // ── Mobile-only filter ─────────────────────────────────────────────
            if (vm.MobileOnly)
                allResults = allResults.Where(b => b.Services.Any(s => s.IsAvailable && s.IsMobile)).ToList();

            // ── Sort ──────────────────────────────────────────────────────────
            allResults = vm.SortBy switch
            {
                "name" => allResults.OrderBy(b => b.Name).ToList(),
                "newest" => allResults.OrderByDescending(b => b.CreatedAt).ToList(),
                _ => allResults.OrderByDescending(b => b.AverageRating).ToList() // default: rating
            };

            vm.Results = allResults;
            vm.TotalCount = allResults.Count;

            return View(vm);
        }

        // ── GET /Provider/Details/{id} ─────────────────────────────────────────

        /// <summary>
        /// Rich profile page for a single provider. Loads local data from DB
        /// and enriches it with Google Places data via the caching service.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var barbershop = await _context.Barbershops
                .Include(b => b.Services)
                .Include(b => b.Reviews)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(b => b.Id == id && b.IsActive);

            if (barbershop == null)
                return NotFound();

            var vm = new ProviderDetailsViewModel
            {
                Barbershop = barbershop
            };

            // ── Enrich with Google Places data ─────────────────────────────────
            if (!string.IsNullOrWhiteSpace(barbershop.PlaceId))
            {
                var googleData = await _googlePlaces.GetPlaceDetailsAsync(barbershop.PlaceId);
                if (googleData != null)
                {
                    vm.GoogleRating = googleData.Rating;
                    vm.GoogleUserRatingsTotal = googleData.UserRatingsTotal;
                    vm.GoogleMapsUrl = googleData.GoogleMapsUrl;
                    vm.GoogleReviews = googleData.Reviews;
                }
            }

            return View(vm);
        }
    }
}
