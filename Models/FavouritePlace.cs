using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    /// <summary>
    /// Represents a barbershop/hairdresser that a user has saved to their favourites.
    /// Only the Google Place ID is persisted — live data is fetched on demand via the Places API.
    /// </summary>
    public class FavouritePlace
    {
        public int Id { get; set; }

        /// <summary>FK to <see cref="ApplicationUser"/>.</summary>
        [Required]
        public string UserId { get; set; } = string.Empty;

        /// <summary>Google Places placeId string (e.g. "ChIJ...").</summary>
        [Required]
        [StringLength(300)]
        public string PlaceId { get; set; } = string.Empty;

        /// <summary>Display name cached at save time for quick list rendering.</summary>
        [Required]
        [StringLength(200)]
        public string PlaceName { get; set; } = string.Empty;

        /// <summary>Address cached at save time for quick list rendering.</summary>
        [StringLength(400)]
        public string? PlaceAddress { get; set; }

        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        // ── Navigation property ───────────────────────────────────────────────────
        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }
    }
}
