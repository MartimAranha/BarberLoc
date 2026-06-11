namespace WebApplication1.Models.ViewModels
{
    /// <summary>
    /// Passed to the map Razor view as the strongly-typed model.
    /// Contains all data needed to render the map and populate markers
    /// without any additional server round-trips on initial load.
    /// </summary>
    public class MapPageViewModel
    {
        /// <summary>
        /// Flat list of barbershop ViewModels serialised to JavaScript on the initial page render.
        /// Each entry provides enough data to place a marker; full details are fetched on click.
        /// </summary>
        public IEnumerable<BarberShopPlaceViewModel> NearbyShops { get; set; } = Enumerable.Empty<BarberShopPlaceViewModel>();

        /// <summary>
        /// Google Maps JavaScript API key. Injected into the map script tag server-side.
        /// Never exposed in client-visible JSON — only used in the &lt;script src&gt; tag.
        /// </summary>
        public string GoogleMapsApiKey { get; set; } = string.Empty;

        /// <summary>Default map centre latitude (used when geolocation is unavailable).</summary>
        public double DefaultLatitude { get; set; } = 38.7169;

        /// <summary>Default map centre longitude.</summary>
        public double DefaultLongitude { get; set; } = -9.1399;

        /// <summary>
        /// Initial Leaflet zoom level. 14 = city-district granularity, ideal for Lisbon barbershop density.
        /// </summary>
        public int DefaultZoom { get; set; } = 14;

        /// <summary>
        /// True when a valid <c>Google:PlacesApiKey</c> is configured and live API data is available.
        /// False in Demo Mode — the UI uses this to show a badge on the sidebar header.
        /// </summary>
        public bool HasApiKey { get; set; }

        /// <summary>
        /// True when no valid Google Places API key is configured (or <c>Google:DemoMode = true</c>
        /// is explicitly set in appsettings). Controls the demo-mode banner and mock-data indicators.
        /// </summary>
        public bool IsDemoMode { get; set; }
    }
}
