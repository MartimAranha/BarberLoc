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
        /// Comma-separated category filter: "Barbershop", "HairSalon", "Unisex".
        /// Empty means all categories.
        /// </summary>
        [Display(Name = "Categoria")]
        public List<string> SelectedCategories { get; set; } = new();

        /// <summary>
        /// Gender filter: "Male", "Female", "Unisex".
        /// Empty means all genders.
        /// </summary>
        [Display(Name = "Género")]
        public List<string> SelectedGenders { get; set; } = new();

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
