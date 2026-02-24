using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class BarbershopsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BarbershopsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Barbershops
        public async Task<IActionResult> Index(string searchString, string sortOrder)
        {
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["RatingSortParm"] = sortOrder == "Rating" ? "rating_desc" : "Rating";
            ViewData["CurrentFilter"] = searchString;

            var barbershops = from b in _context.Barbershops
                            .Include(b => b.Reviews)
                            select b;

            if (!String.IsNullOrEmpty(searchString))
            {
                barbershops = barbershops.Where(b => b.Name.Contains(searchString)
                                       || b.Address.Contains(searchString));
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
            return View();
        }

        // GET: Barbershops/GetMapData
        [HttpGet]
        public async Task<JsonResult> GetMapData()
        {
            var barbershops = await _context.Barbershops
                .Where(b => b.IsActive)
                .Select(b => new 
                { 
                    id = b.Id, 
                    name = b.Name, 
                    lat = b.Latitude, 
                    lng = b.Longitude,
                    address = b.Address,
                    category = b.Category.ToString(), // "Barbershop", "HairSalon", "Unisex"
                    rating = b.AverageRating,
                    image = b.ImageUrl
                })
                .ToListAsync();

            return Json(barbershops);
        }

        // GET: Barbershops/ClearAllBarbershops - Admin action to remove all seeded barbershops
        [HttpPost]
        public async Task<IActionResult> ClearAllBarbershops()
        {
            // Delete all reviews first (referential integrity)
            var reviews = _context.Reviews.ToList();
            _context.Reviews.RemoveRange(reviews);

            // Delete all services
            var services = _context.Services.ToList();
            _context.Services.RemoveRange(services);

            // Delete all bookings
            var bookings = _context.Bookings.ToList();
            _context.Bookings.RemoveRange(bookings);

            // Delete all barbershops
            var barbershops = _context.Barbershops.ToList();
            _context.Barbershops.RemoveRange(barbershops);

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
