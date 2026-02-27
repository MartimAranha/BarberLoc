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

        // GET: Bookings/Create
        public async Task<IActionResult> Create(int? barbershopId)
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
            
            return View(new Booking { BarbershopId = barbershop.Id });
        }

        // POST: Bookings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BarbershopId,ServiceId,BookingDate,BookingTime,Notes")] Booking booking)
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

            _context.Add(booking);
            await _context.SaveChangesAsync();
            
            TempData["Success"] = "Reserva criada com sucesso! Aguarde a confirmação da barbearia.";
            return RedirectToAction(nameof(Index));
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
