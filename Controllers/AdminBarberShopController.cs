using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminBarberShopController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IGooglePlacesService _googlePlacesService;

        public AdminBarberShopController(ApplicationDbContext context, IGooglePlacesService googlePlacesService)
        {
            _context = context;
            _googlePlacesService = googlePlacesService;
        }

        // GET: AdminBarberShop
        public async Task<IActionResult> Index()
        {
            var shops = await _context.Barbershops
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new BarberShopVerificationViewModel
                {
                    Id = b.Id,
                    Name = b.Name,
                    Address = b.Address,
                    GooglePlaceId = b.GooglePlaceId,
                    OperationalStatus = b.OperationalStatus,
                    LastVerifiedAt = b.LastVerifiedAt,
                    IsActive = b.IsActive,
                    Category = b.Category
                })
                .ToListAsync();

            return View(shops);
        }

        // POST: AdminBarberShop/Verify/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(int id)
        {
            var shop = await _context.Barbershops.FindAsync(id);
            if (shop == null) return NotFound();

            var result = await _googlePlacesService.VerifyPlaceStatusAsync(shop.GooglePlaceId);

            shop.OperationalStatus = result.Status;
            shop.LastVerifiedAt = DateTime.UtcNow;

            // Automatically deactivate if permanently closed
            if (result.Status == OperationalStatus.PermanentlyClosed)
            {
                shop.IsActive = false;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Estado de '{shop.Name}' atualizado para: {result.Status}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: AdminBarberShop/VerifyAll
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyAll()
        {
            // Verify up to 20 unverified or oldest verified shops to avoid quota limits
            var shopsToVerify = await _context.Barbershops
                .OrderBy(b => b.LastVerifiedAt.HasValue)
                .ThenBy(b => b.LastVerifiedAt)
                .Take(20)
                .ToListAsync();

            var summary = new GoogleSyncSummaryViewModel { TotalProcessed = shopsToVerify.Count };

            foreach (var shop in shopsToVerify)
            {
                var result = await _googlePlacesService.VerifyPlaceStatusAsync(shop.GooglePlaceId);

                shop.OperationalStatus = result.Status;
                shop.LastVerifiedAt = DateTime.UtcNow;

                if (result.Status == OperationalStatus.PermanentlyClosed)
                {
                    shop.IsActive = false;
                    summary.PermanentlyClosedCount++;
                }
                else if (result.Status == OperationalStatus.Active)
                {
                    summary.ActiveCount++;
                }
                else if (result.Status == OperationalStatus.TemporarilyClosed)
                {
                    summary.TemporarilyClosedCount++;
                }
                else
                {
                    summary.UnverifiedCount++;
                    if (!result.IsLive) summary.ErrorCount++;
                }
            }

            if (shopsToVerify.Any())
            {
                await _context.SaveChangesAsync();
            }

            TempData["SyncSummary"] = $"Verificação concluída. Processados: {summary.TotalProcessed}. Activos: {summary.ActiveCount}, Fechados: {summary.PermanentlyClosedCount}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: AdminBarberShop/SetStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(int id, OperationalStatus status, bool isActive)
        {
            var shop = await _context.Barbershops.FindAsync(id);
            if (shop == null) return NotFound();

            shop.OperationalStatus = status;
            shop.IsActive = isActive;
            
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"O estado de '{shop.Name}' foi alterado manualmente.";
            return RedirectToAction(nameof(Index));
        }
    }
}
