using System.ComponentModel.DataAnnotations;

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
        
        [Phone]
        public string? PhoneNumber { get; set; }
        
        [EmailAddress]
        public string? Email { get; set; }
        
        public string? OpeningHours { get; set; }
        
        public string? ImageUrl { get; set; }
        // Optional Google Place ID when available (for reviews and direct map links)
        // New canonical GooglePlaceId used as the unique mapping to Google Places.
        [Required]
        [StringLength(100)]
        public string GooglePlaceId { get; set; } = string.Empty;

        // Existing legacy PlaceId property retained for compatibility with older code.
        public string? PlaceId { get; set; }
        
        // AverageRating kept for historical compatibility. New optional Rating field stores
        // the latest Google Places rating when available.
        public double AverageRating { get; set; } = 0;

        public double? Rating { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public BarbershopCategory Category { get; set; } = BarbershopCategory.Barbershop;
        
        // Navigation properties
        public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();
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

    public enum TargetGender
    {
        Male,
        Female,
        Unisex
    }
}
