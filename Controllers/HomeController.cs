using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    /// <summary>
    /// Landing page controller.
    /// The home page is a fully static hero/marketing page. All dynamic barbershop
    /// data is fetched live from the Google Places API and rendered on the /Map page.
    /// This controller intentionally does NOT query the Barbershops DB table on the
    /// landing page so that startup is fast and no stale data is surfaced.
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IGooglePlacesService _placesService;
        private readonly IConfiguration _config;

        public HomeController(
            ILogger<HomeController> logger,
            IGooglePlacesService placesService,
            IConfiguration config)
        {
            _logger       = logger;
            _placesService = placesService;
            _config       = config;
        }

        // GET /  or  GET /Home/Index
        // Renders the landing page. No DB queries — all dynamic content lives at /Map.
        public IActionResult Index()
        {
            // Determine whether a live API key is configured so the view can adapt its CTA copy.
            var hasApiKey = !string.IsNullOrWhiteSpace(
                _config["Google:PlacesApiKey"] ?? _config["Google:ApiKey"]);

            ViewData["Title"]     = "Início";
            ViewData["HasApiKey"] = hasApiKey;

            return View();
        }

        // GET /Home/Privacy
        public IActionResult Privacy()
        {
            return View();
        }

        // GET /Home/Error
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
