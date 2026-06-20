using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("admin/diagnostics")]
    public class DiagnosticsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public DiagnosticsController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var canConnect = await _context.Database.CanConnectAsync();

            var vm = new DiagnosticsViewModel
            {
                EnvironmentName = _env.EnvironmentName,
                DatabaseConnected = canConnect,
                DatabaseProvider = _context.Database.ProviderName ?? "Unknown",
                CurrentTime = DateTime.UtcNow
            };

            return View(vm);
        }
    }

    public class DiagnosticsViewModel
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public bool DatabaseConnected { get; set; }
        public string DatabaseProvider { get; set; } = string.Empty;
        public DateTime CurrentTime { get; set; }
    }
}
