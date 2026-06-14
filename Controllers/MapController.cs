using Microsoft.AspNetCore.Mvc;

namespace WebApplication1.Controllers
{
    public class MapController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Search");
        }

        [HttpGet]
        public IActionResult GetMarkers()
        {
            return Json(new object[] { });
        }

        [HttpGet]
        public IActionResult GetLiveMarkers()
        {
            return Json(new object[] { });
        }
        
        [HttpGet]
        public IActionResult GetDetails(int id)
        {
            return NotFound();
        }

        [HttpGet]
        public IActionResult Details(string? placeId)
        {
            return NotFound();
        }
    }
}
