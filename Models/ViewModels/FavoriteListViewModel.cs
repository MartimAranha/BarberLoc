using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels
{
    public class FavoriteListViewModel
    {
        public int BarbershopId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public double? GoogleRating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public bool? IsOpenNow { get; set; }
        public DateTime FavoritedAt { get; set; }
        public string? PlaceId { get; set; }
    }
}
