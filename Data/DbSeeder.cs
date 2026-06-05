using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            // Seed Roles
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
            }
            if (!await roleManager.RoleExistsAsync("User"))
            {
                await roleManager.CreateAsync(new IdentityRole("User"));
            }

            // Seed Admin User
            if (await userManager.FindByEmailAsync("admin@barberloc.pt") == null)
            {
                var adminUser = new ApplicationUser
                {
                    UserName = "admin@barberloc.pt",
                    Email = "admin@barberloc.pt",
                    FullName = "Administrador",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.Now
                };
                await userManager.CreateAsync(adminUser, "Admin123!");
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }

            // Seed Sample User
            if (await userManager.FindByEmailAsync("joao@example.com") == null)
            {
                var sampleUser = new ApplicationUser
                {
                    UserName = "joao@example.com",
                    Email = "joao@example.com",
                    FullName = "João Silva",
                    DateOfBirth = new DateTime(1995, 5, 15),
                    Address = "Lisboa, Portugal",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.Now
                };
                await userManager.CreateAsync(sampleUser, "User123!");
                await userManager.AddToRoleAsync(sampleUser, "User");
            }

            // Seed Barbershops and Services from Apify
            var hasMissingPlaceIds = await context.Barbershops.AnyAsync(b => b.PlaceId == null);
            if (!context.Barbershops.Any() || hasMissingPlaceIds)
            {
                try
                {
                    // Clear existing seeded data to avoid schema mismatch/missing PlaceId
                    if (context.Bookings.Any())
                    {
                        context.Bookings.RemoveRange(context.Bookings);
                    }
                    if (context.Reviews.Any())
                    {
                        context.Reviews.RemoveRange(context.Reviews);
                    }
                    if (context.Services.Any())
                    {
                        context.Services.RemoveRange(context.Services);
                    }
                    if (context.Barbershops.Any())
                    {
                        context.Barbershops.RemoveRange(context.Barbershops);
                    }
                    await context.SaveChangesAsync();

                    using var httpClient = new HttpClient();
                    var url = "https://api.apify.com/v2/datasets/R3lD8rmitYhFrPtZT/items?token=apify_api_dkdK1JkxP4vMGAcxVfnN9NLTnENPBo1ND0AK";
                    var response = await httpClient.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var places = System.Text.Json.JsonSerializer.Deserialize<List<ApifyPlace>>(json, options);
                        
                        if (places != null)
                        {
                            foreach (var place in places)
                            {
                                // Only add if it has a valid location
                                if (place.location == null) continue;

                                var isSalon = place.title != null && (place.title.ToLower().Contains("salon") || place.title.ToLower().Contains("cabeleireiro"));
                                
                                var barbershop = new Barbershop
                                {
                                    Name = place.title ?? "Barbearia",
                                    Description = !string.IsNullOrEmpty(place.website) ? $"Website: {place.website}" : null,
                                    Address = place.address ?? "Endereço indisponível",
                                    Latitude = place.location.lat,
                                    Longitude = place.location.lng,
                                    PhoneNumber = place.phone,
                                    ImageUrl = place.imageUrl,
                                    AverageRating = place.totalScore ?? 0,
                                    PlaceId = place.placeId,
                                    Category = isSalon ? BarbershopCategory.HairSalon : BarbershopCategory.Barbershop,
                                    IsActive = true,
                                    CreatedAt = DateTime.Now
                                };

                                context.Barbershops.Add(barbershop);
                                await context.SaveChangesAsync();

                                // Add customized services based on Category
                                var services = new List<Service>();
                                if (isSalon)
                                {
                                    services.Add(new Service { Name = "Corte Feminino", Description = "Corte e lavagem especializada para senhora", Price = 25.00m, DurationMinutes = 45, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = true, TargetGender = TargetGender.Female });
                                    services.Add(new Service { Name = "Corte Masculino", Description = "Corte e estilização clássica de homem", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = true, TargetGender = TargetGender.Male });
                                    services.Add(new Service { Name = "Penteado & Styling", Description = "Penteados e secagem profissional", Price = 30.00m, DurationMinutes = 60, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = false, TargetGender = TargetGender.Female });
                                    services.Add(new Service { Name = "Coloração Completa", Description = "Tratamento de cor premium", Price = 45.00m, DurationMinutes = 90, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = false, TargetGender = TargetGender.Female });
                                }
                                else
                                {
                                    services.Add(new Service { Name = "Corte de Cabelo", Description = "Corte moderno ou clássico com tesoura e máquina", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = true, TargetGender = TargetGender.Male });
                                    services.Add(new Service { Name = "Aparar Barba", Description = "Barba tradicional com toalha quente e navalha", Price = 10.00m, DurationMinutes = 20, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = false, TargetGender = TargetGender.Male });
                                    services.Add(new Service { Name = "Cabelo & Barba", Description = "Combo completo de corte e cuidado de barba", Price = 22.00m, DurationMinutes = 50, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = true, TargetGender = TargetGender.Male });
                                    services.Add(new Service { Name = "Corte Infantil", Description = "Corte especial para crianças até 12 anos", Price = 12.00m, DurationMinutes = 25, IsAvailable = true, BarbershopId = barbershop.Id, IsMobile = true, TargetGender = TargetGender.Unisex });
                                }
                                context.Services.AddRange(services);
                            }
                            await context.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error seeding from Apify: {ex.Message}");
                }
            }
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
