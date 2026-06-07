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
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public BookingsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ── GET: /Bookings ─────────────────────────────────────────────────────
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

        // ── GET: /Bookings/Create?barbershopId={id} ────────────────────────────
        public async Task<IActionResult> Create(int? barbershopId)
        {
            if (barbershopId == null)
                return NotFound();

            var barbershop = await _context.Barbershops
                .Include(b => b.Services)
                .FirstOrDefaultAsync(b => b.Id == barbershopId && b.IsActive);

            if (barbershop == null)
                return NotFound();

            var vm = new AppointmentCreateViewModel
            {
                BarbershopId = barbershop.Id,
                Barbershop = barbershop,
                AvailableServices = barbershop.Services.Where(s => s.IsAvailable).ToList()
            };

            return View(vm);
        }

        // ── POST: /Bookings/Create ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AppointmentCreateViewModel vm)
        {
            // Reload barbershop for display and additional validation
            var barbershop = await _context.Barbershops
                .Include(b => b.Services)
                .FirstOrDefaultAsync(b => b.Id == vm.BarbershopId && b.IsActive);

            if (barbershop == null)
                return NotFound();

            vm.Barbershop = barbershop;
            vm.AvailableServices = barbershop.Services.Where(s => s.IsAvailable).ToList();

            // ── Additional custom validation ───────────────────────────────────
            if (vm.BookingDate.Date < DateTime.Today.AddDays(1))
                ModelState.AddModelError(nameof(vm.BookingDate), "A data da reserva deve ser a partir de amanhã.");

            if (vm.IsOnSite && vm.ServiceId.HasValue)
            {
                var selectedService = barbershop.Services.FirstOrDefault(s => s.Id == vm.ServiceId);
                if (selectedService != null && !selectedService.IsMobile)
                    ModelState.AddModelError(nameof(vm.ServiceId), "O serviço selecionado não está disponível ao domicílio. Escolha um serviço com domicílio ou desative a opção.");
            }

            if (!ModelState.IsValid)
                return View(vm);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Challenge();

            // ── Build the Booking entity ──────────────────────────────────────
            var booking = new Booking
            {
                UserId = user.Id,
                BarbershopId = vm.BarbershopId,
                ServiceId = vm.ServiceId,
                BookingDate = vm.BookingDate,
                BookingTime = vm.BookingTime,
                Notes = vm.Notes,
                IsOnSite = vm.IsOnSite,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.Now
            };

            // ── Compute travel fee if home service requested ───────────────────
            if (booking.IsOnSite && vm.UserLat.HasValue && vm.UserLng.HasValue)
            {
                var dist = HaversineDistance(vm.UserLat.Value, vm.UserLng.Value, barbershop.Latitude, barbershop.Longitude);
                booking.TravelDistanceKm = Math.Round(dist, 2);
                booking.TravelFee = Math.Round(5.0m + (decimal)dist * 0.75m, 2);
            }

            _context.Add(booking);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Reserva criada com sucesso! Aguarde a confirmação da barbearia.";
            return RedirectToAction(nameof(Index));
        }

        // ── GET: /Bookings/EstimateFee ─────────────────────────────────────────
        [HttpGet]
        public async Task<JsonResult> EstimateFee(int barbershopId, double userLat, double userLng)
        {
            var shop = await _context.Barbershops.FindAsync(barbershopId);
            if (shop == null) return Json(new { distanceKm = 0, fee = 5 });

            var dist = HaversineDistance(userLat, userLng, shop.Latitude, shop.Longitude);
            var fee = Math.Round(5.0m + (decimal)dist * 0.75m, 2);
            return Json(new { distanceKm = Math.Round(dist, 2), fee });
        }

        // ── POST: /Bookings/Cancel/{id} ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null || booking.UserId != user!.Id)
                return NotFound();

            if (booking.Status == BookingStatus.Completed)
            {
                TempData["Error"] = "Não é possível cancelar uma reserva já concluída.";
                return RedirectToAction(nameof(Index));
            }

            booking.Status = BookingStatus.Cancelled;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Reserva cancelada com sucesso.";
            return RedirectToAction(nameof(Index));
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
}
