using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IConfiguration config, ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IGooglePlacesService? googlePlacesService = null)
        {
            // Seed Roles
            if (!await roleManager.RoleExistsAsync("Admin")) await roleManager.CreateAsync(new IdentityRole("Admin"));
            if (!await roleManager.RoleExistsAsync("User")) await roleManager.CreateAsync(new IdentityRole("User"));

            // Seed Admin User
            ApplicationUser? sampleUser = null;
            if (await userManager.FindByEmailAsync("admin@barberloc.pt") == null)
            {
                var adminUser = new ApplicationUser { UserName = "admin@barberloc.pt", Email = "admin@barberloc.pt", FullName = "Administrador", EmailConfirmed = true, CreatedAt = DateTime.Now };
                await userManager.CreateAsync(adminUser, "Admin123!");
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }

            // Seed primary sample user (used for bookings and reviews)
            if (await userManager.FindByEmailAsync("joao@example.com") == null)
            {
                var newSampleUser = new ApplicationUser { UserName = "joao@example.com", Email = "joao@example.com", FullName = "João Silva", DateOfBirth = new DateTime(1995, 5, 15), Address = "Lisboa, Portugal", EmailConfirmed = true, CreatedAt = DateTime.Now };
                await userManager.CreateAsync(newSampleUser, "User123!");
                await userManager.AddToRoleAsync(newSampleUser, "User");
                sampleUser = newSampleUser;
            }
            else
            {
                sampleUser = await userManager.FindByEmailAsync("joao@example.com");
            }



            // ── Seed Barbershops ─────────────────────────────────────────────────
            var hasMissingPlaceIds = await context.Barbershops.AnyAsync(b => b.PlaceId == null);
            if (!context.Barbershops.Any() || hasMissingPlaceIds)
            {
                try
                {
                    // Clear existing dependent data before reseeding
                    context.Bookings.RemoveRange(context.Bookings);
                    context.Reviews.RemoveRange(context.Reviews);
                    context.Services.RemoveRange(context.Services);
                    context.Barbershops.RemoveRange(context.Barbershops);
                    await context.SaveChangesAsync();

                    // Obtain Apify configuration safely
                    var apifyToken = config["Apify:Token"];
                    var apifyBase  = config["Apify:DatasetUrl"];

                    if (!string.IsNullOrWhiteSpace(apifyBase))
                    {
                        var url = !string.IsNullOrWhiteSpace(apifyToken) ? $"{apifyBase}?token={apifyToken}" : apifyBase;
                        using var httpClient = new HttpClient();
                        var response = await httpClient.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var places = System.Text.Json.JsonSerializer.Deserialize<List<ApifyPlace>>(json, options);

                            if (places != null && places.Count > 0)
                            {
                                foreach (var place in places.Where(p => p.location != null))
                                {
                                    var isSalon = (place.title ?? "").ToLower().Contains("salon") || (place.title ?? "").ToLower().Contains("cabeleireiro");

                                    // GooglePlaceId: prefer the API-supplied placeId; synthesise a mock ID
                                    // when the Apify dataset omits it so the NOT NULL constraint is satisfied.
                                    var googlePlaceId = !string.IsNullOrWhiteSpace(place.placeId)
                                        ? place.placeId
                                        : $"ChIJApify_{(place.title ?? "place").Replace(" ", "_")[..Math.Min(20, (place.title ?? "place").Length)]}_{Guid.NewGuid():N}"[..Math.Min(100, 100)];

                                    var barbershop = new Barbershop
                                    {
                                        Name          = place.title ?? "Barbearia",
                                        Description   = !string.IsNullOrEmpty(place.website) ? $"Website: {place.website}" : null,
                                        Address       = place.address ?? "Endereço indisponível",
                                        Latitude      = place.location!.lat,
                                        Longitude     = place.location.lng,
                                        PhoneNumber   = place.phone,
                                        ImageUrl      = place.imageUrl,
                                        AverageRating = place.totalScore ?? 0,
                                        PlaceId       = place.placeId,
                                        GooglePlaceId = googlePlaceId,
                                        Rating        = place.totalScore,
                                        Category      = isSalon ? BarbershopCategory.HairSalon : BarbershopCategory.Barbershop,
                                        OperationalStatus = OperationalStatus.Unverified,
                                        IsActive      = true,
                                        CreatedAt     = DateTime.Now,
                                        UpdatedAt     = DateTime.Now
                                    };

                                    context.Barbershops.Add(barbershop);
                                    await context.SaveChangesAsync();

                                    var services = GetServicesForCategory(barbershop.Id, isSalon);
                                    context.Services.AddRange(services);
                                    await context.SaveChangesAsync();
                                }
                            }
                        }
                    }

                    // ── Live Google Places seeding: prefer real-time data when API key present ─
                    var apiKey = config["Google:PlacesApiKey"] ?? config["Google:ApiKey"];

                    if (!string.IsNullOrWhiteSpace(apiKey) && googlePlacesService != null)
                    {
                        try
                        {
                            // Default seed area: Lisbon centre
                            var lat = 38.7169;
                            var lng = -9.1399;
                            var radius = 5000; // 5km

                            var places = await googlePlacesService.FetchLiveBarbershopsAsync(lat, lng, radius);
                            if (places != null && places.Count > 0)
                            {
                                // Insert top N unique places into Barbershop table
                                var top = places.Take(12);
                                foreach (var p in top)
                                {
                                    if (string.IsNullOrWhiteSpace(p.PlaceId)) continue;

                                    var exists = await context.Barbershops.AnyAsync(b => b.GooglePlaceId == p.PlaceId);
                                    if (exists) continue;

                                    var isSalon = (p.Name ?? "").ToLower().Contains("salon") || (p.Name ?? "").ToLower().Contains("cabeleireiro");

                                    var barbershop = new Barbershop
                                    {
                                        Name = p.Name ?? "Barbearia",
                                        Description = !string.IsNullOrEmpty(p.Address) ? p.Address : null,
                                        Address = p.Address ?? "Endereço indisponível",
                                        Latitude = p.Lat,
                                        Longitude = p.Lng,
                                        PhoneNumber = null,
                                        ImageUrl = null,
                                        AverageRating = p.Rating ?? 0,
                                        GooglePlaceId = p.PlaceId,
                                        Category = isSalon ? BarbershopCategory.HairSalon : BarbershopCategory.Barbershop,
                                        OperationalStatus = OperationalStatus.Unverified,
                                        IsActive = true,
                                        CreatedAt = DateTime.Now,
                                        UpdatedAt = DateTime.Now
                                    };

                                    context.Barbershops.Add(barbershop);
                                    await context.SaveChangesAsync();

                                    // Add default services for new provider
                                    var services = GetServicesForCategory(barbershop.Id, isSalon);
                                    context.Services.AddRange(services);
                                    await context.SaveChangesAsync();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DbSeeder] Live seeding failed: {ex.Message}");
                        }
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DbSeeder] Error during seeding: {ex.Message}");
                }
            }
        }

        // ── Service Generator for Apify-sourced providers ────────────────────────

        private static List<Service> GetServicesForCategory(int barbershopId, bool isSalon)
        {
            var services = new List<Service>();
            if (isSalon)
            {
                services.Add(new Service { Name = "Corte Feminino",  Description = "Corte e lavagem especializada para senhora", Price = 25.00m, DurationMinutes = 45, IsAvailable = true, BarbershopId = barbershopId, IsMobile = true,  TargetGender = TargetGender.Female });
                services.Add(new Service { Name = "Corte Masculino", Description = "Corte e estilização clássica de homem",      Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershopId, IsMobile = true,  TargetGender = TargetGender.Male });
            }
            else
            {
                services.Add(new Service { Name = "Corte de Cabelo", Description = "Corte moderno ou clássico",                 Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershopId, IsMobile = true,  TargetGender = TargetGender.Male });
                services.Add(new Service { Name = "Aparar Barba",    Description = "Barba tradicional com toalha quente",       Price = 10.00m, DurationMinutes = 20, IsAvailable = true, BarbershopId = barbershopId, IsMobile = false, TargetGender = TargetGender.Male });
            }
            return services;
        }
    }

    public class ApifyPlace
    {
        public string? title { get; set; }
        public string? address { get; set; }
        public ApifyLocation? location { get; set; }
        public double? totalScore { get; set; }
        public string? imageUrl { get; set; }
        public string? phone { get; set; }
        public string? website { get; set; }
        public string? placeId { get; set; }
    }

    public class ApifyLocation
    {
        public double lat { get; set; }
        public double lng { get; set; }
    }
}