using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models.ViewModels
{
    /// <summary>
    /// ViewModel for the Provider search and discovery page (Provider/Index).
    /// Carries both the filter inputs and the resulting list.
    /// </summary>
    public class ProviderSearchViewModel
    {
        // ── Filter Inputs ──────────────────────────────────────────────────────

        /// <summary>Free-text search against name, description, and address.</summary>
        [Display(Name = "Pesquisar")]
        public string? SearchQuery { get; set; }

        /// <summary>
        /// Service/Gender filter: "Todos", "Barbearia", "Cabeleireiro".
        /// </summary>
        [Display(Name = "Serviço / Género")]
        public string ServiceGender { get; set; } = "Todos";

        /// <summary>Search radius in kilometers (e.g. 2, 5, 10, 20).</summary>
        [Display(Name = "Distância")]
        public int RadiusInKm { get; set; } = 15;

        /// <summary>If true, only show providers that offer at least one mobile/home service.</summary>
        [Display(Name = "Apenas ao Domicílio")]
        public bool MobileOnly { get; set; } = false;

        /// <summary>Minimum average rating filter (1–5). Null means no minimum.</summary>
        [Display(Name = "Avaliação mínima")]
        [Range(1, 5)]
        public double? MinRating { get; set; }

        // ── Sort ───────────────────────────────────────────────────────────────
        [Display(Name = "Ordenar por")]
        public string SortBy { get; set; } = "rating"; // "rating" | "name" | "newest"

        // ── Results ────────────────────────────────────────────────────────────
        public List<Barbershop> Results { get; set; } = new();
        public int TotalCount { get; set; }
        public bool HasResults => Results.Any();
    }
}
