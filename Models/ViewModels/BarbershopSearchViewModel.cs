namespace WebApplication1.Models.ViewModels
{
    public class BarbershopSearchViewModel
    {
        public string PlaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? PhotoUrl { get; set; }
        public string? Category { get; set; }
    }
}
