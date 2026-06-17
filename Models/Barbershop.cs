using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    public class Barbershop
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Address { get; set; } = string.Empty;
        
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        
        public string? ImageUrl { get; set; }
        // Optional Google Place ID when available (for reviews and direct map links)
        // New canonical GooglePlaceId used as the unique mapping to Google Places.
        [Required]
        [StringLength(100)]
        public string GooglePlaceId { get; set; } = string.Empty;

        [NotMapped]
        public double? Rating { get; set; }

        [NotMapped]
        public int? UserRatingsTotal { get; set; }

        // Existing legacy PlaceId property retained for compatibility with older code.
        public string? PlaceId { get; set; }
        
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Operational status sourced from Google Places API <c>business_status</c> field.
        /// Defaults to <see cref="OperationalStatus.Active"/> so existing rows are not disrupted.
        /// </summary>
        public OperationalStatus OperationalStatus { get; set; } = OperationalStatus.Active;

        /// <summary>UTC timestamp of the last successful Google Places verification call for this shop.</summary>
        public DateTime? LastVerifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public BarbershopCategory Category { get; set; } = BarbershopCategory.Barbershop;
        
        // Navigation properties
        public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public virtual ICollection<Service> Services { get; set; } = new List<Service>();
    }

    public enum BarbershopCategory
    {
        [Display(Name = "Barbearia")]
        Barbershop,
        [Display(Name = "Cabeleireiro")]
        HairSalon,
        [Display(Name = "Unisexo")]
        Unisex
    }

    /// <summary>
    /// Reflects the Google Places API <c>business_status</c> field.
    /// Values map directly to the string constants returned by the Places API.
    /// </summary>
    public enum OperationalStatus
    {
        /// <summary>Place is open and accepting customers (OPERATIONAL).</summary>
        [Display(Name = "Activo")]
        Active = 0,

        /// <summary>Place is permanently closed (CLOSED_PERMANENTLY).</summary>
        [Display(Name = "Fechado Permanentemente")]
        PermanentlyClosed = 1,

        /// <summary>Place is temporarily closed (CLOSED_TEMPORARILY).</summary>
        [Display(Name = "Fechado Temporariamente")]
        TemporarilyClosed = 2,

        /// <summary>Status not yet verified against the Google Places API.</summary>
        [Display(Name = "Não Verificado")]
        Unverified = 3
    }

    public enum TargetGender
    {
        Male,
        Female,
        Unisex
    }
}
