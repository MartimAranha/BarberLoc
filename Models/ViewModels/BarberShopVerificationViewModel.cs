using WebApplication1.Models;

namespace WebApplication1.Models.ViewModels
{
    public class BarberShopVerificationViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string GooglePlaceId { get; set; } = string.Empty;
        
        public OperationalStatus OperationalStatus { get; set; }
        public DateTime? LastVerifiedAt { get; set; }
        
        public bool IsActive { get; set; }
        public BarbershopCategory Category { get; set; }
    }
}
