using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IConfiguration config, ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
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

            // Seed Sample User
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
                    var apifyBase = config["Apify:DatasetUrl"];
                    var apifySeeded = false;

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

                                    var barbershop = new Barbershop
                                    {
                                        Name = place.title ?? "Barbearia",
                                        Description = !string.IsNullOrEmpty(place.website) ? $"Website: {place.website}" : null,
                                        Address = place.address ?? "Endereço indisponível",
                                        Latitude = place.location!.lat,
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

                                    var services = GetServicesForCategory(barbershop.Id, isSalon);
                                    context.Services.AddRange(services);
                                }
                                await context.SaveChangesAsync();
                                apifySeeded = true;
                            }
                        }
                    }

                    // ── Hardcoded fallback: seed 3 providers if Apify unavailable ─
                    if (!apifySeeded)
                    {
                        await SeedFallbackProvidersAsync(context, sampleUser);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DbSeeder] Error seeding from Apify: {ex.Message}. Seeding fallback providers.");
                    // Ensure fallback seeding even if the try block partially executed
                    if (!context.Barbershops.Any())
                    {
                        await SeedFallbackProvidersAsync(context, sampleUser);
                    }
                }
            }

            // ── Seed FavouritePlaces ─────────────────────────────────────────────
            await SeedFavouritePlacesAsync(context, sampleUser);
        }

        // ── Seed FavouritePlaces for the sample user ─────────────────────────────

        private static async Task SeedFavouritePlacesAsync(ApplicationDbContext context, ApplicationUser? sampleUser)
        {
            if (sampleUser == null) return;
            if (await context.FavouritePlaces.AnyAsync(f => f.UserId == sampleUser.Id)) return;

            context.FavouritePlaces.AddRange(new[]
            {
                new FavouritePlace
                {
                    UserId = sampleUser.Id,
                    PlaceId = "ChIJBarbershopMockLisboa001",
                    PlaceName = "Barbearia Clássica Lisboa",
                    PlaceAddress = "Rua Augusta 120, 1100-053 Lisboa",
                    SavedAt = DateTime.UtcNow.AddDays(-30)
                },
                new FavouritePlace
                {
                    UserId = sampleUser.Id,
                    PlaceId = "ChIJHairSalonMockCascais002",
                    PlaceName = "Salão Elegance Cascais",
                    PlaceAddress = "Av. Marginal 55, 2750-341 Cascais",
                    SavedAt = DateTime.UtcNow.AddDays(-14)
                },
                new FavouritePlace
                {
                    UserId = sampleUser.Id,
                    PlaceId = "ChIJBarbershopMockPorto003",
                    PlaceName = "UrbanCuts Porto",
                    PlaceAddress = "Rua de Santa Catarina 300, 4000-447 Porto",
                    SavedAt = DateTime.UtcNow.AddDays(-7)
                }
            });

            await context.SaveChangesAsync();
        }

        // ── Fallback Hardcoded Seed Data ─────────────────────────────────────────

        private static async Task SeedFallbackProvidersAsync(ApplicationDbContext context, ApplicationUser? sampleUser)
        {
            // ── Provider 1: Barbearia Clássica Lisboa ────────────────────────────
            var provider1 = new Barbershop
            {
                Name = "Barbearia Clássica Lisboa",
                Description = "Barbearia tradicional no coração de Lisboa. Cortes modernos e clássicos, tratamentos de barba e ambiente premium.",
                Address = "Rua Augusta 120, 1100-053 Lisboa",
                Latitude = 38.7100,
                Longitude = -9.1380,
                PhoneNumber = "+351 21 000 1111",
                Email = "classica@barberloc.pt",
                OpeningHours = "Seg-Sex: 09:00–20:00 | Sáb: 09:00–18:00",
                ImageUrl = "https://images.unsplash.com/photo-1585747860715-2ba37e788b70?w=800&q=80",
                AverageRating = 4.8,
                PlaceId = "ChIJBarbershopMockLisboa001",
                Category = BarbershopCategory.Barbershop,
                IsActive = true,
                CreatedAt = DateTime.Now.AddMonths(-6)
            };
            context.Barbershops.Add(provider1);
            await context.SaveChangesAsync();

            context.Services.AddRange(new[]
            {
                new Service { BarbershopId = provider1.Id, Name = "Corte de Cabelo", Description = "Corte moderno ou clássico com acabamento perfeito.", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, IsMobile = true, TargetGender = TargetGender.Male },
                new Service { BarbershopId = provider1.Id, Name = "Aparar Barba", Description = "Barba tradicional com toalha quente e navalha.", Price = 10.00m, DurationMinutes = 20, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Male },
                new Service { BarbershopId = provider1.Id, Name = "Corte + Barba", Description = "Pack completo com desconto especial.", Price = 22.00m, DurationMinutes = 45, IsAvailable = true, IsMobile = true, TargetGender = TargetGender.Male }
            });
            await context.SaveChangesAsync();

            // ── Provider 2: Salão Elegance Cascais ──────────────────────────────
            var provider2 = new Barbershop
            {
                Name = "Salão Elegance Cascais",
                Description = "Cabeleireiro de luxo em Cascais. Especialistas em coloração, penteados e tratamentos capilares para toda a família.",
                Address = "Av. Marginal 55, 2750-341 Cascais",
                Latitude = 38.6968,
                Longitude = -9.4207,
                PhoneNumber = "+351 21 000 2222",
                Email = "elegance@barberloc.pt",
                OpeningHours = "Ter-Sáb: 10:00–19:00 | Dom: 10:00–14:00",
                ImageUrl = "https://images.unsplash.com/photo-1562322140-8baeececf3df?w=800&q=80",
                AverageRating = 4.6,
                PlaceId = "ChIJHairSalonMockCascais002",
                Category = BarbershopCategory.HairSalon,
                IsActive = true,
                CreatedAt = DateTime.Now.AddMonths(-4)
            };
            context.Barbershops.Add(provider2);
            await context.SaveChangesAsync();

            context.Services.AddRange(new[]
            {
                new Service { BarbershopId = provider2.Id, Name = "Corte Feminino", Description = "Corte e lavagem especializada para senhora.", Price = 28.00m, DurationMinutes = 50, IsAvailable = true, IsMobile = true, TargetGender = TargetGender.Female },
                new Service { BarbershopId = provider2.Id, Name = "Coloração Completa", Description = "Coloração com produtos premium e acabamento brilhante.", Price = 65.00m, DurationMinutes = 120, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Female },
                new Service { BarbershopId = provider2.Id, Name = "Corte Masculino", Description = "Corte e estilização clássica de homem.", Price = 18.00m, DurationMinutes = 30, IsAvailable = true, IsMobile = true, TargetGender = TargetGender.Male }
            });
            await context.SaveChangesAsync();

            // ── Provider 3: UrbanCuts Porto ──────────────────────────────────────
            var provider3 = new Barbershop
            {
                Name = "UrbanCuts Porto",
                Description = "Barbearia moderna no Porto. Cortes urbanos, fade e serviço ao domicílio disponível em toda a cidade.",
                Address = "Rua de Santa Catarina 300, 4000-447 Porto",
                Latitude = 41.1496,
                Longitude = -8.6109,
                PhoneNumber = "+351 22 000 3333",
                Email = "urbancutsporto@barberloc.pt",
                OpeningHours = "Seg-Sáb: 09:00–21:00",
                ImageUrl = "https://images.unsplash.com/photo-1503951914875-452162b0f3f1?w=800&q=80",
                AverageRating = 4.5,
                PlaceId = "ChIJBarbershopMockPorto003",
                Category = BarbershopCategory.Unisex,
                IsActive = true,
                CreatedAt = DateTime.Now.AddMonths(-2)
            };
            context.Barbershops.Add(provider3);
            await context.SaveChangesAsync();

            context.Services.AddRange(new[]
            {
                new Service { BarbershopId = provider3.Id, Name = "Fade / Degradê", Description = "Fade skin, low, mid ou high com acabamento premium.", Price = 18.00m, DurationMinutes = 40, IsAvailable = true, IsMobile = true, TargetGender = TargetGender.Male },
                new Service { BarbershopId = provider3.Id, Name = "Corte Unisexo", Description = "Corte moderno para qualquer género.", Price = 20.00m, DurationMinutes = 35, IsAvailable = true, IsMobile = true, TargetGender = TargetGender.Unisex },
                new Service { BarbershopId = provider3.Id, Name = "Tratamento de Barba", Description = "Hidratação, aparar e modelação completa.", Price = 12.00m, DurationMinutes = 25, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Male }
            });
            await context.SaveChangesAsync();

            // ── Seed 2 sample bookings (require a registered user) ───────────────
            if (sampleUser != null)
            {
                // Only seed if there are no bookings yet
                if (!await context.Bookings.AnyAsync())
                {
                    var service1 = await context.Services
                        .FirstOrDefaultAsync(s => s.BarbershopId == provider1.Id && s.Name == "Corte de Cabelo");
                    var service3 = await context.Services
                        .FirstOrDefaultAsync(s => s.BarbershopId == provider3.Id && s.Name == "Fade / Degradê");

                    context.Bookings.Add(new Booking
                    {
                        UserId = sampleUser.Id,
                        BarbershopId = provider1.Id,
                        ServiceId = service1?.Id,
                        BookingDate = DateTime.Today.AddDays(3),
                        BookingTime = new TimeSpan(10, 30, 0),
                        Status = BookingStatus.Confirmed,
                        Notes = "Preferência por corte clássico com produto.",
                        IsOnSite = false,
                        CreatedAt = DateTime.Now.AddDays(-1)
                    });

                    context.Bookings.Add(new Booking
                    {
                        UserId = sampleUser.Id,
                        BarbershopId = provider3.Id,
                        ServiceId = service3?.Id,
                        BookingDate = DateTime.Today.AddDays(7),
                        BookingTime = new TimeSpan(14, 0, 0),
                        Status = BookingStatus.Pending,
                        Notes = "Serviço ao domicílio — apartamento no 3º andar.",
                        IsOnSite = true,
                        TravelDistanceKm = 5.2,
                        TravelFee = 8.90m,
                        CreatedAt = DateTime.Now
                    });

                    await context.SaveChangesAsync();
                }
            }
        }

        // ── Service Generator for Apify-sourced providers ────────────────────────

        private static List<Service> GetServicesForCategory(int barbershopId, bool isSalon)
        {
            var services = new List<Service>();
            if (isSalon)
            {
                services.Add(new Service { Name = "Corte Feminino", Description = "Corte e lavagem especializada para senhora", Price = 25.00m, DurationMinutes = 45, IsAvailable = true, BarbershopId = barbershopId, IsMobile = true, TargetGender = TargetGender.Female });
                services.Add(new Service { Name = "Corte Masculino", Description = "Corte e estilização clássica de homem", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershopId, IsMobile = true, TargetGender = TargetGender.Male });
            }
            else
            {
                services.Add(new Service { Name = "Corte de Cabelo", Description = "Corte moderno ou clássico", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershopId, IsMobile = true, TargetGender = TargetGender.Male });
                services.Add(new Service { Name = "Aparar Barba", Description = "Barba tradicional com toalha quente", Price = 10.00m, DurationMinutes = 20, IsAvailable = true, BarbershopId = barbershopId, IsMobile = false, TargetGender = TargetGender.Male });
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