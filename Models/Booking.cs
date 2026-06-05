using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    public class Booking
    {
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        [Required]
        public int BarbershopId { get; set; }
        
        public int? ServiceId { get; set; }
        
        [Required]
        public DateTime BookingDate { get; set; }
        
        [Required]
        public TimeSpan BookingTime { get; set; }
        
        public BookingStatus Status { get; set; } = BookingStatus.Pending;
        
        [StringLength(500)]
        public string? Notes { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // On-site booking fields
        public bool IsOnSite { get; set; } = false;
        public double? TravelDistanceKm { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? TravelFee { get; set; }
        
        // Navigation properties
        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; } = null!;
        
        [ForeignKey("BarbershopId")]
        public virtual Barbershop Barbershop { get; set; } = null!;
        
        [ForeignKey("ServiceId")]
        public virtual Service? Service { get; set; }
    }
    
    public enum BookingStatus
    {
        Pending,
        Confirmed,
        Cancelled,
        Completed
    }
}
