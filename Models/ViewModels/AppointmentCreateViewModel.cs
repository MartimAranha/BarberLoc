using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels
{
    /// <summary>
    /// Strongly-typed ViewModel for the appointment creation form (Bookings/Create).
    /// Replaces the previous ViewBag-based approach to enable model binding and
    /// server-side validation annotations.
    /// </summary>
    public class AppointmentCreateViewModel
    {
        // ── Hidden / Routing Fields ────────────────────────────────────────────
        public int BarbershopId { get; set; }

        // ── Barbershop Context (populated by controller, not bound from form) ──
        public Barbershop? Barbershop { get; set; }
        public List<Service> AvailableServices { get; set; } = new();

        // ── Booking Fields ─────────────────────────────────────────────────────
        [Display(Name = "Serviço")]
        public int? ServiceId { get; set; }

        [Required(ErrorMessage = "A data da reserva é obrigatória.")]
        [Display(Name = "Data da Reserva")]
        [DataType(DataType.Date)]
        public DateTime BookingDate { get; set; } = DateTime.Today.AddDays(1);

        [Required(ErrorMessage = "A hora da reserva é obrigatória.")]
        [Display(Name = "Hora da Reserva")]
        [DataType(DataType.Time)]
        public TimeSpan BookingTime { get; set; } = new TimeSpan(10, 0, 0);

        [Display(Name = "Notas Adicionais")]
        [StringLength(500, ErrorMessage = "As notas não podem ter mais de 500 caracteres.")]
        public string? Notes { get; set; }

        // ── Home Service Fields ────────────────────────────────────────────────
        [Display(Name = "Serviço ao Domicílio")]
        public bool IsOnSite { get; set; } = false;

        /// <summary>User's latitude, captured client-side via Geolocation API.</summary>
        public double? UserLat { get; set; }

        /// <summary>User's longitude, captured client-side via Geolocation API.</summary>
        public double? UserLng { get; set; }

        // ── Computed / Display Fields ──────────────────────────────────────────

        /// <summary>Whether any of the barbershop's services support home visits.</summary>
        public bool HasMobileServices => AvailableServices.Any(s => s.IsMobile && s.IsAvailable);

        /// <summary>Estimated travel distance in km (populated after user grants location).</summary>
        public double? EstimatedDistanceKm { get; set; }

        /// <summary>Estimated travel fee in EUR.</summary>
        public decimal? EstimatedTravelFee { get; set; }
    }
}
