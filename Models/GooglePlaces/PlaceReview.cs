namespace WebApplication1.Models.GooglePlaces
{
    /// <summary>
    /// A single Google reviewer entry from the Places Details API.
    /// </summary>
    public class PlaceReview
    {
        public string AuthorName { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }
        public int Rating { get; set; }
        public string? RelativeTimeDescription { get; set; }
        public string? Text { get; set; }
        public long? Time { get; set; }
    }
}
