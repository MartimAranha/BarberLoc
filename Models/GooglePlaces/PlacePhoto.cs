namespace WebApplication1.Models.GooglePlaces
{
    /// <summary>
    /// A single photo reference from the Google Places Details API.
    /// Use <see cref="GetProxyUrl"/> to build a server-side proxy URL that keeps the API key hidden.
    /// </summary>
    public class PlacePhoto
    {
        public string PhotoReference { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// Returns the server-side photo proxy URL for this photo reference.
        /// The proxy keeps the Google API key off the client.
        /// </summary>
        /// <param name="maxWidth">Maximum width in pixels (default 800).</param>
        public string GetProxyUrl(int maxWidth = 800) =>
            $"/Barbershops/PlacePhoto?ref={Uri.EscapeDataString(PhotoReference)}&maxWidth={maxWidth}";

        /// <summary>
        /// Builds the direct Google Places Photo URL. 
        /// Only used server-side (e.g. inside the proxy endpoint) — never exposed to the browser.
        /// </summary>
        public string GetGoogleUrl(string apiKey, int maxWidth = 800) =>
            $"https://maps.googleapis.com/maps/api/place/photo?maxwidth={maxWidth}&photo_reference={Uri.EscapeDataString(PhotoReference)}&key={apiKey}";
    }
}
