using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    public class Service
    {
        public int Id { get; set; }
        
        [Required]
        public int BarbershopId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(300)]
        public string? Description { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
        
        public int DurationMinutes { get; set; }
        
        public bool IsAvailable { get; set; } = true;

        public bool IsHomeService { get; set; } = false;
        // Indicates whether the service can be provided at customer's home
        public bool IsMobile { get; set; } = false;

        // Target gender for the service: Male, Female, Unisex
        public TargetGender TargetGender { get; set; } = TargetGender.Unisex;
        
        // Navigation property
        [ForeignKey("BarbershopId")]
        public virtual Barbershop Barbershop { get; set; } = null!;
    }
}
