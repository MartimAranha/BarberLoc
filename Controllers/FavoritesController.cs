using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers
{
    [Authorize]
    public class FavoritesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public FavoritesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: /Favorites
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // 1. Fetch Local DB Favorites
            var localFavorites = await _context.FavoriteBarbershops
                .Include(f => f.Barbershop)
                .Where(f => f.UserId == user.Id)
                .Select(f => new FavoriteListViewModel
                {
                    BarbershopId = f.Barbershop.Id,
                    Name = f.Barbershop.Name,
                    Address = f.Barbershop.Address,
                    ImageUrl = f.Barbershop.ImageUrl,
                    GoogleRating = f.Barbershop.Rating, 
                    UserRatingsTotal = f.Barbershop.UserRatingsTotal,
                    FavoritedAt = f.CreatedAt,
                    PlaceId = null // Local DB
                })
                .ToListAsync();

            // 2. Fetch Google Places Favorites
            var placesFavorites = await _context.FavouritePlaces
                .Where(f => f.UserId == user.Id)
                .Select(f => new FavoriteListViewModel
                {
                    BarbershopId = 0, // Not a local DB entity
                    Name = f.PlaceName ?? "Barbearia / Cabeleireiro",
                    Address = f.PlaceAddress ?? "Morada não disponível",
                    ImageUrl = null, // We'll let the view show a placeholder or we can fetch it dynamically (but fetching all might be slow)
                    GoogleRating = null,
                    UserRatingsTotal = null,
                    FavoritedAt = f.SavedAt,
                    PlaceId = f.PlaceId
                })
                .ToListAsync();

            // 3. Combine and sort
            var allFavorites = localFavorites.Concat(placesFavorites)
                .OrderByDescending(f => f.FavoritedAt)
                .ToList();

            return View(allFavorites);
        }

        // POST: /Favorites/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int barbershopId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var exists = await _context.FavoriteBarbershops
                .AnyAsync(f => f.UserId == user.Id && f.BarbershopId == barbershopId);

            if (!exists)
            {
                var favorite = new FavoriteBarbershop
                {
                    UserId = user.Id,
                    BarbershopId = barbershopId,
                    CreatedAt = DateTime.Now
                };
                _context.FavoriteBarbershops.Add(favorite);
                await _context.SaveChangesAsync();
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, isFavorited = true });
            }

            return RedirectToAction("Details", "Barbershops", new { id = barbershopId });
        }

        // POST: /Favorites/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int barbershopId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var favorite = await _context.FavoriteBarbershops
                .FirstOrDefaultAsync(f => f.UserId == user.Id && f.BarbershopId == barbershopId);

            if (favorite != null)
            {
                _context.FavoriteBarbershops.Remove(favorite);
                await _context.SaveChangesAsync();
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, isFavorited = false });
            }

            // Redirect back to referring page or index
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                return Redirect(referer);
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Favorites/RemovePlace
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePlace(string placeId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var favorite = await _context.FavouritePlaces
                .FirstOrDefaultAsync(f => f.UserId == user.Id && f.PlaceId == placeId);

            if (favorite != null)
            {
                _context.FavouritePlaces.Remove(favorite);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
