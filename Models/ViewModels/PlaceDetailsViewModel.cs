using System.Collections.Generic;
using WebApplication1.Models.GooglePlaces;

namespace WebApplication1.Models.ViewModels
{
    public class PlaceDetailsViewModel
    {
        public string PlaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? FormattedAddress { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Website { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string? GoogleMapsUrl { get; set; }
        public bool? IsOpenNow { get; set; }
        public List<string> WeekdayText { get; set; } = new();
        public List<PlacePhoto> Photos { get; set; } = new();
        public List<PlaceReview> Reviews { get; set; } = new();
        public double? Lat { get; set; }
        public double? Lng { get; set; }
    }
}
