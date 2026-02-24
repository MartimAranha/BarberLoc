using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize]
    public class ReviewsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Reviews/Create
        public async Task<IActionResult> Create(int? barbershopId)
        {
            if (barbershopId == null)
            {
                return NotFound();
            }

            var barbershop = await _context.Barbershops.FindAsync(barbershopId);
            if (barbershop == null)
            {
                return NotFound();
            }

            ViewBag.Barbershop = barbershop;
            return View(new Review { BarbershopId = barbershop.Id });
        }

        // POST: Reviews/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BarbershopId,Rating,Comment")] Review review)
        {
            var user = await _userManager.GetUserAsync(User);
            
            if (user == null)
            {
                return Challenge();
            }

            review.UserId = user.Id;
            review.CreatedAt = DateTime.Now;

            _context.Add(review);
            await _context.SaveChangesAsync();
            
            // Update barbershop average rating
            await UpdateBarbershopRating(review.BarbershopId);
            
            TempData["Success"] = "Avaliação adicionada com sucesso!";
            return RedirectToAction("Details", "Barbershops", new { id = review.BarbershopId });
        }

        private async Task UpdateBarbershopRating(int barbershopId)
        {
            var barbershop = await _context.Barbershops
                .Include(b => b.Reviews)
                .FirstOrDefaultAsync(b => b.Id == barbershopId);
                
            if (barbershop != null && barbershop.Reviews.Any())
            {
                barbershop.AverageRating = barbershop.Reviews.Average(r => r.Rating);
                await _context.SaveChangesAsync();
            }
        }
    }
}
