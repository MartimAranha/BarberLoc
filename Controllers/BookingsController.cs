using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize]
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public BookingsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Bookings
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var bookings = await _context.Bookings
                .Include(b => b.Barbershop)
                .Include(b => b.Service)
                .Where(b => b.UserId == user!.Id)
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();
                
            return View(bookings);
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

        // GET: Bookings/Create
        public async Task<IActionResult> Create(int? barbershopId, double? userLat, double? userLng)
        {
            if (barbershopId == null)
            {
                return NotFound();
            }

            var barbershop = await _context.Barbershops
                .Include(b => b.Services)
                .FirstOrDefaultAsync(b => b.Id == barbershopId);
                
            if (barbershop == null)
            {
                return NotFound();
            }

            ViewBag.Barbershop = barbershop;
            ViewBag.Services = barbershop.Services.Where(s => s.IsAvailable).ToList();
            // default travel estimate not calculated here; will compute on POST if IsOnSite true
            
            return View(new Booking { BarbershopId = barbershop.Id });
        }

        // POST: Bookings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BarbershopId,ServiceId,BookingDate,BookingTime,Notes,IsOnSite")] Booking booking, double? userLat, double? userLng)
        {
            var user = await _userManager.GetUserAsync(User);
            
            if (!ModelState.IsValid)
            {
                var barbershop = await _context.Barbershops
                    .Include(b => b.Services)
                    .FirstOrDefaultAsync(b => b.Id == booking.BarbershopId);
                
                ViewBag.Barbershop = barbershop;
                ViewBag.Services = barbershop?.Services.Where(s => s.IsAvailable).ToList();
                return View(booking);
            }

            if (user == null)
            {
                return Challenge();
            }

            booking.UserId = user.Id;
            booking.Status = BookingStatus.Pending;
            booking.CreatedAt = DateTime.Now;
            // Guardar telefone do utilizador se existir para uso posterior (por exemplo, mostrar na confirmação)
            if (!string.IsNullOrEmpty(user.PhoneNumber))
            {
                ViewBag.UserPhone = user.PhoneNumber;
            }


            // If on-site requested, compute travel distance and fee (straight-line approximation)
            if (booking.IsOnSite && userLat.HasValue && userLng.HasValue)
            {
                var shop = await _context.Barbershops.FindAsync(booking.BarbershopId);
                if (shop != null)
                {
                    var dist = HaversineDistance(userLat.Value, userLng.Value, shop.Latitude, shop.Longitude);
                    booking.TravelDistanceKm = Math.Round(dist, 2);
                    booking.TravelFee = Math.Round(5.0m + (decimal)dist * 0.75m, 2);
                }
            }

            _context.Add(booking);
            await _context.SaveChangesAsync();
            
            TempData["Success"] = "Reserva criada com sucesso! Aguarde a confirmação da barbearia.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Bookings/EstimateFee
        [HttpGet]
        public async Task<JsonResult> EstimateFee(int barbershopId, double userLat, double userLng)
        {
            var shop = await _context.Barbershops.FindAsync(barbershopId);
            if (shop == null) return Json(new { distanceKm = 0, fee = 5 });

            var dist = HaversineDistance(userLat, userLng, shop.Latitude, shop.Longitude);
            var fee = Math.Round(5.0m + (decimal)dist * 0.75m, 2);
            return Json(new { distanceKm = Math.Round(dist, 2), fee });
        }

        // POST: Bookings/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings.FindAsync(id);
            
            if (booking == null || booking.UserId != user!.Id)
            {
                return NotFound();
            }

            booking.Status = BookingStatus.Cancelled;
            await _context.SaveChangesAsync();
            
            TempData["Success"] = "Reserva cancelada com sucesso.";
            return RedirectToAction(nameof(Index));
        }
    }
}
