namespace WebApplication1.Models.GooglePlaces
{
    /// <summary>
    /// Full place details DTO returned by <see cref="Services.IGooglePlacesService.GetFullPlaceDetailsAsync"/>.
    /// Covers all fields required to populate the map detail panel.
    /// </summary>
    public class PlaceDetailsResult
    {
        public string PlaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? FormattedAddress { get; set; }
        public string? FormattedPhoneNumber { get; set; }
        public string? Website { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string? GoogleMapsUrl { get; set; }

        /// <summary>Opening hours information from the Places API.</summary>
        public PlaceOpeningHours? OpeningHours { get; set; }

        /// <summary>Up to 5 photo references for the carousel.</summary>
        public List<PlacePhoto> Photos { get; set; } = new();

        /// <summary>Up to 5 Google reviewer entries.</summary>
        public List<PlaceReview> Reviews { get; set; } = new();

        /// <summary>Latitude of the place.</summary>
        public double? Lat { get; set; }

        /// <summary>Longitude of the place.</summary>
        public double? Lng { get; set; }

    }

    /// <summary>
    /// Opening hours information.
    /// </summary>
    public class PlaceOpeningHours
    {
        public bool? IsOpenNow { get; set; }
        public List<string> WeekdayText { get; set; } = new();
    }
}
