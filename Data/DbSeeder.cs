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

            // Seed two extra synthetic reviewer accounts (no password → Google-auth-only profiles)
            // These provide variety in the seeded Review records so the demo panel looks realistic.
            var reviewer2 = await EnsureSyntheticUser(userManager, "ana@example.com", "Ana Costa", new DateTime(1992, 3, 22));
            var reviewer3 = await EnsureSyntheticUser(userManager, "carlos@example.com", "Carlos Mendes", new DateTime(1988, 11, 5));

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
                                        IsActive      = true,
                                        CreatedAt     = DateTime.Now,
                                        UpdatedAt     = DateTime.Now
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

                    // ── Live Google Places seeding: prefer real-time data when API key present ─
                    var apiKey = config["Google:PlacesApiKey"] ?? config["Google:ApiKey"];
                    var liveSeeded = false;

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

                                liveSeeded = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DbSeeder] Live seeding failed: {ex.Message}");
                            liveSeeded = false;
                        }
                    }

                    // If neither Apify nor Live seeding occurred, fallback to hardcoded providers
                    if (!apifySeeded && !liveSeeded)
                    {
                        await SeedFallbackProvidersAsync(context, sampleUser, reviewer2, reviewer3);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DbSeeder] Error seeding from Apify: {ex.Message}. Seeding fallback providers.");
                    // Ensure fallback seeding even if the try block partially executed
                    if (!context.Barbershops.Any())
                    {
                        await SeedFallbackProvidersAsync(context, sampleUser, reviewer2, reviewer3);
                    }
                }
            }

            // ── Seed FavouritePlaces ─────────────────────────────────────────────
            await SeedFavouritePlacesAsync(context, sampleUser);

            // ── Seed BarberShopPlaces (Google Places cache / map markers) ─────────
            // Pass IConfiguration so the seeder can detect API keys and configuration values
            await SeedBarberShopPlacesAsync(context, config);

            // ── Seed additional Barbershop provider records (idempotent by PlaceId) ─
            await SeedAdditionalBarbershopsAsync(context, sampleUser, reviewer2, reviewer3);
        }

        // ── Synthetic reviewer helper ────────────────────────────────────────────

        /// <summary>
        /// Creates a Google-auth-only user account (no password) for use as a seed reviewer.
        /// Idempotent — returns the existing user if already present.
        /// </summary>
        private static async Task<ApplicationUser?> EnsureSyntheticUser(
            UserManager<ApplicationUser> userManager, string email, string fullName, DateTime dob)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing != null) return existing;

            var user = new ApplicationUser
            {
                UserName        = email,
                Email           = email,
                FullName        = fullName,
                DateOfBirth     = dob,
                EmailConfirmed  = true,
                CreatedAt       = DateTime.Now
            };

            // No password — these accounts are Google-auth-only synthetic reviewers
            var result = await userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");
                return user;
            }

            Console.WriteLine($"[DbSeeder] Warning: could not create synthetic user {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            return null;
        }

        // ── Seed BarberShopPlaces (map markers cache table) ─────────────────────

        private static async Task SeedBarberShopPlacesAsync(ApplicationDbContext context, IConfiguration config)
        {
            // When a live Google Places API key is configured, skip inserting mock PlaceIds.
            // The GetLiveMarkers endpoint will populate BarberShopPlaces with real data on first map pan.
            // We still seed if the table is empty AND no API key is present, so the map is never blank.
            var apiKey = config["Google:PlacesApiKey"] ?? config["Google:ApiKey"] ?? string.Empty;
            var hasLiveKey = !string.IsNullOrWhiteSpace(apiKey);

            if (hasLiveKey)
            {
                // Live mode: only seed if the table is completely empty (i.e. first-ever run).
                // Once GetLiveMarkers fires, real ChIJ... PlaceIds replace these as the primary data source.
                if (await context.BarberShopPlaces.AnyAsync()) return;
            }
            else
            {
                // No-key mode: ensure we have at least the full 12-record fallback set.
                if (await context.BarberShopPlaces.CountAsync() >= 12) return;

                // Remove any partial seed to avoid unique-index violations on PlaceId
                context.BarberShopPlaces.RemoveRange(context.BarberShopPlaces);
                await context.SaveChangesAsync();
            }

            // ── Shared opening-hours JSON strings ─────────────────────────────────
            var standardHoursJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                "Segunda-feira: 09:00 – 20:00",
                "Terça-feira: 09:00 – 20:00",
                "Quarta-feira: 09:00 – 20:00",
                "Quinta-feira: 09:00 – 20:00",
                "Sexta-feira: 09:00 – 20:00",
                "Sábado: 09:00 – 18:00",
                "Domingo: Fechado"
            });

            var eleganceHoursJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                "Segunda-feira: Fechado",
                "Terça-feira: 10:00 – 19:00",
                "Quarta-feira: 10:00 – 19:00",
                "Quinta-feira: 10:00 – 19:00",
                "Sexta-feira: 10:00 – 19:00",
                "Sábado: 10:00 – 18:00",
                "Domingo: 10:00 – 14:00"
            });

            var urbanHoursJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                "Segunda-feira: 09:00 – 21:00",
                "Terça-feira: 09:00 – 21:00",
                "Quarta-feira: 09:00 – 21:00",
                "Quinta-feira: 09:00 – 21:00",
                "Sexta-feira: 09:00 – 21:00",
                "Sábado: 09:00 – 21:00",
                "Domingo: Fechado"
            });

            var braganoBraHoursJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                "Segunda-feira: 10:00 – 19:00",
                "Terça-feira: 10:00 – 19:00",
                "Quarta-feira: 10:00 – 19:00",
                "Quinta-feira: 10:00 – 19:00",
                "Sexta-feira: 10:00 – 20:00",
                "Sábado: 09:00 – 17:00",
                "Domingo: Fechado"
            });

            context.BarberShopPlaces.AddRange(new[]
            {
                // ── 1: Barbearia Clássica Lisboa (Baixa) ────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockLisboa001",
                    Name             = "Barbearia Clássica Lisboa",
                    Address          = "Rua Augusta 120, 1100-053 Lisboa",
                    PhoneNumber      = "+351 21 000 1111",
                    Website          = "https://www.barberloc.pt",
                    Rating           = 4.8,
                    UserRatingsTotal = 214,
                    Latitude         = 38.7100,
                    Longitude        = -9.1380,
                    OpeningHoursJson = standardHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Barbershop,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 2: Salão Elegance Cascais ────────────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJHairSalonMockCascais002",
                    Name             = "Salão Elegance Cascais",
                    Address          = "Av. Marginal 55, 2750-341 Cascais",
                    PhoneNumber      = "+351 21 000 2222",
                    Website          = "https://www.elegancecascais.pt",
                    Rating           = 4.6,
                    UserRatingsTotal = 87,
                    Latitude         = 38.6968,
                    Longitude        = -9.4207,
                    OpeningHoursJson = eleganceHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.HairSalon,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 3: UrbanCuts Porto (Bolhão) ──────────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockPorto003",
                    Name             = "UrbanCuts Porto",
                    Address          = "Rua de Santa Catarina 300, 4000-447 Porto",
                    PhoneNumber      = "+351 22 000 3333",
                    Website          = "https://www.urbancutsporto.pt",
                    Rating           = 4.5,
                    UserRatingsTotal = 163,
                    Latitude         = 41.1496,
                    Longitude        = -8.6109,
                    OpeningHoursJson = urbanHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Unisex,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 4: Barbearia Príncipe Real (Lisboa) ──────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockLisboa004",
                    Name             = "Barbearia Príncipe Real",
                    Address          = "Rua da Escola Politécnica 42, 1250-100 Lisboa",
                    PhoneNumber      = "+351 21 000 4444",
                    Website          = "https://www.barbeariaprincipereal.pt",
                    Rating           = 4.7,
                    UserRatingsTotal = 98,
                    Latitude         = 38.7183,
                    Longitude        = -9.1502,
                    OpeningHoursJson = standardHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Barbershop,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 5: Cabeleireiro Alfama (Lisboa) ──────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJHairSalonMockAlfama005",
                    Name             = "Cabeleireiro Alfama",
                    Address          = "Rua de São João da Praça 10, 1100-521 Lisboa",
                    PhoneNumber      = "+351 21 000 5555",
                    Website          = null,
                    Rating           = 4.3,
                    UserRatingsTotal = 52,
                    Latitude         = 38.7118,
                    Longitude        = -9.1320,
                    OpeningHoursJson = eleganceHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.HairSalon,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 6: Fade Factory Lisboa (Mouraria) ────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockMouraria006",
                    Name             = "Fade Factory Lisboa",
                    Address          = "Rua do Benformoso 198, 1100-084 Lisboa",
                    PhoneNumber      = "+351 21 000 6666",
                    Website          = "https://www.fadefactory.pt",
                    Rating           = 4.9,
                    UserRatingsTotal = 311,
                    Latitude         = 38.7151,
                    Longitude        = -9.1351,
                    OpeningHoursJson = urbanHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Barbershop,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 7: Studio Unisex Braga ───────────────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJUnisexMockBraga007",
                    Name             = "Studio Unisex Braga",
                    Address          = "Rua do Souto 80, 4700-239 Braga",
                    PhoneNumber      = "+351 25 300 7777",
                    Website          = "https://www.studiounisexbraga.pt",
                    Rating           = 4.4,
                    UserRatingsTotal = 74,
                    Latitude         = 41.5503,
                    Longitude        = -8.4200,
                    OpeningHoursJson = braganoBraHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Unisex,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 8: Barbearia NorteSul (Setúbal) ─────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockSetubal008",
                    Name             = "Barbearia NorteSul",
                    Address          = "Av. Luísa Todi 180, 2900-451 Setúbal",
                    PhoneNumber      = "+351 26 500 8888",
                    Website          = null,
                    Rating           = 4.2,
                    UserRatingsTotal = 39,
                    Latitude         = 38.5243,
                    Longitude        = -8.8882,
                    OpeningHoursJson = standardHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Barbershop,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 9: Barbearia do Intendente (Lisboa, Intendente/Mouraria) ────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockIntendente009",
                    Name             = "Barbearia do Intendente",
                    Address          = "Largo do Intendente Pina Manique 12, 1100-285 Lisboa",
                    PhoneNumber      = "+351 21 000 9999",
                    Website          = "https://www.barbeariadointendente.pt",
                    Rating           = 4.6,
                    UserRatingsTotal = 127,
                    Latitude         = 38.7187,
                    Longitude        = -9.1327,
                    OpeningHoursJson = standardHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Barbershop,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 10: Salão Belém Premium (Lisboa, Belém) ──────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJHairSalonMockBelem010",
                    Name             = "Salão Belém Premium",
                    Address          = "Rua de Belém 22, 1300-085 Lisboa",
                    PhoneNumber      = "+351 21 000 1010",
                    Website          = "https://www.salaobelem.pt",
                    Rating           = 4.4,
                    UserRatingsTotal = 61,
                    Latitude         = 38.6966,
                    Longitude        = -9.2049,
                    OpeningHoursJson = eleganceHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.HairSalon,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 11: CutStyle Odivelas ────────────────────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJBarbershopMockOdivelas011",
                    Name             = "CutStyle Odivelas",
                    Address          = "Av. Amália Rodrigues 5, 2675-309 Odivelas",
                    PhoneNumber      = "+351 21 933 1111",
                    Website          = null,
                    Rating           = 4.3,
                    UserRatingsTotal = 48,
                    Latitude         = 38.7924,
                    Longitude        = -9.1816,
                    OpeningHoursJson = braganoBraHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Barbershop,
                    LastFetchedAt    = DateTime.UtcNow
                },

                // ── 12: Unisex Hub Almada ────────────────────────────────────────────────
                new BarberShopPlace
                {
                    PlaceId          = "ChIJUnisexMockAlmada012",
                    Name             = "Unisex Hub Almada",
                    Address          = "Praça Gil Vicente 3, 2800-159 Almada",
                    PhoneNumber      = "+351 21 274 1212",
                    Website          = "https://www.unisexhub.pt",
                    Rating           = 4.5,
                    UserRatingsTotal = 89,
                    Latitude         = 38.6741,
                    Longitude        = -9.1576,
                    OpeningHoursJson = urbanHoursJson,
                    PhotoReference   = null,
                    Category         = BarbershopCategory.Unisex,
                    LastFetchedAt    = DateTime.UtcNow
                }
            });

            await context.SaveChangesAsync();
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
                    UserId       = sampleUser.Id,
                    PlaceId      = "ChIJBarbershopMockLisboa001",
                    PlaceName    = "Barbearia Clássica Lisboa",
                    PlaceAddress = "Rua Augusta 120, 1100-053 Lisboa",
                    SavedAt      = DateTime.UtcNow.AddDays(-30)
                },
                new FavouritePlace
                {
                    UserId       = sampleUser.Id,
                    PlaceId      = "ChIJHairSalonMockCascais002",
                    PlaceName    = "Salão Elegance Cascais",
                    PlaceAddress = "Av. Marginal 55, 2750-341 Cascais",
                    SavedAt      = DateTime.UtcNow.AddDays(-14)
                },
                new FavouritePlace
                {
                    UserId       = sampleUser.Id,
                    PlaceId      = "ChIJBarbershopMockPorto003",
                    PlaceName    = "UrbanCuts Porto",
                    PlaceAddress = "Rua de Santa Catarina 300, 4000-447 Porto",
                    SavedAt      = DateTime.UtcNow.AddDays(-7)
                },
                new FavouritePlace
                {
                    UserId       = sampleUser.Id,
                    PlaceId      = "ChIJBarbershopMockMouraria006",
                    PlaceName    = "Fade Factory Lisboa",
                    PlaceAddress = "Rua do Benformoso 198, 1100-084 Lisboa",
                    SavedAt      = DateTime.UtcNow.AddDays(-2)
                }
            });

            await context.SaveChangesAsync();
        }

        // ── Additional Barbershop providers (idempotent by PlaceId) ───────────────
        // Adds providers 4 and 5 if they are not already in the DB.
        // Safe to run on existing databases — checks PlaceId existence before inserting.
        private static async Task SeedAdditionalBarbershopsAsync(
            ApplicationDbContext context,
            ApplicationUser?     sampleUser,
            ApplicationUser?     reviewer2,
            ApplicationUser?     reviewer3)
        {
            // ── Provider 4: Barbearia do Intendente ──────────────────────────────
            if (!await context.Barbershops.AnyAsync(b => b.PlaceId == "ChIJBarbershopMockIntendente009"))
            {
                var provider4 = new Barbershop
                {
                    Name          = "Barbearia do Intendente",
                    Description   = "Barbearia de bairro no histórico Largo do Intendente. Cortes tradicionais e modernos num espaço acolhedor e autêntico.",
                    Address       = "Largo do Intendente Pina Manique 12, 1100-285 Lisboa",
                    Latitude      = 38.7187,
                    Longitude     = -9.1327,
                    PhoneNumber   = "+351 21 000 9999",
                    Email         = "intendente@barberloc.pt",
                    OpeningHours  = "Seg-Sex: 09:00–20:00 | Sáb: 09:00–18:00",
                    ImageUrl      = "https://images.unsplash.com/photo-1621605815971-fbc98d665033?w=800&q=80",
                    AverageRating = 4.6,
                    PlaceId       = "ChIJBarbershopMockIntendente009",
                    GooglePlaceId = "ChIJBarbershopMockIntendente009",
                    Rating        = 4.6,
                    Category      = BarbershopCategory.Barbershop,
                    IsActive      = true,
                    CreatedAt     = DateTime.Now.AddMonths(-3),
                    UpdatedAt     = DateTime.Now
                };
                context.Barbershops.Add(provider4);
                await context.SaveChangesAsync();

                context.Services.AddRange(new[]
                {
                    new Service { BarbershopId = provider4.Id, Name = "Corte de Cabelo",    Description = "Corte clássico ou moderno com acabamento perfeito.",          Price = 14.00m, DurationMinutes = 30, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Male },
                    new Service { BarbershopId = provider4.Id, Name = "Barba Tradicional",  Description = "Barba à navalha com toalha quente e produtos premium.",        Price = 11.00m, DurationMinutes = 25, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Male },
                    new Service { BarbershopId = provider4.Id, Name = "Corte + Barba",      Description = "Pack completo — corte e barba com desconto.",                   Price = 23.00m, DurationMinutes = 50, IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Male }
                });
                await context.SaveChangesAsync();

                await SeedReviewsForShop(context, provider4.Id, new[]
                {
                    (sampleUser, 5, "Lugar incrível no Intendente! O barbeiro conhece bem o bairro e o trabalho é excelente.",          DateTime.Now.AddDays(-4)),
                    (reviewer3,  4, "Ambiente muito autêntico, preços acessíveis e atendimento simpático. Voltarei com certeza.",        DateTime.Now.AddDays(-15)),
                    (reviewer2,  5, "A melhor barba que fiz em Lisboa. Navalha limpa e produto de qualidade. 5 estrelas merecidas.",     DateTime.Now.AddDays(-28))
                });
            }

            // ── Provider 5: Salão Belém Premium ─────────────────────────────────
            if (!await context.Barbershops.AnyAsync(b => b.PlaceId == "ChIJHairSalonMockBelem010"))
            {
                var provider5 = new Barbershop
                {
                    Name          = "Salão Belém Premium",
                    Description   = "Cabeleireiro premium junto ao mosteiro dos Jerónimos. Especialistas em coloração, cortes femininos e tratamentos capilares.",
                    Address       = "Rua de Belém 22, 1300-085 Lisboa",
                    Latitude      = 38.6966,
                    Longitude     = -9.2049,
                    PhoneNumber   = "+351 21 000 1010",
                    Email         = "belem@barberloc.pt",
                    OpeningHours  = "Ter-Sáb: 10:00–19:00 | Dom: 10:00–14:00",
                    ImageUrl      = "https://images.unsplash.com/photo-1604654894610-df63bc536371?w=800&q=80",
                    AverageRating = 4.4,
                    PlaceId       = "ChIJHairSalonMockBelem010",
                    GooglePlaceId = "ChIJHairSalonMockBelem010",
                    Rating        = 4.4,
                    Category      = BarbershopCategory.HairSalon,
                    IsActive      = true,
                    CreatedAt     = DateTime.Now.AddMonths(-1),
                    UpdatedAt     = DateTime.Now
                };
                context.Barbershops.Add(provider5);
                await context.SaveChangesAsync();

                context.Services.AddRange(new[]
                {
                    new Service { BarbershopId = provider5.Id, Name = "Corte Feminino Premium", Description = "Corte e styling com lavagem e máscara hidratante.",          Price = 32.00m, DurationMinutes = 60,  IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Female },
                    new Service { BarbershopId = provider5.Id, Name = "Coloração Global",       Description = "Coloração completa com produtos sem amoníaco.",               Price = 70.00m, DurationMinutes = 120, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Female },
                    new Service { BarbershopId = provider5.Id, Name = "Corte Masculino",        Description = "Corte clássico masculino com acabamento premium.",             Price = 18.00m, DurationMinutes = 30,  IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Male }
                });
                await context.SaveChangesAsync();

                await SeedReviewsForShop(context, provider5.Id, new[]
                {
                    (reviewer2, 5, "O melhor salão de Belém! Coloração perfeita e o espaço é absolutamente lindo.",                    DateTime.Now.AddDays(-6)),
                    (sampleUser, 4, "Serviço de qualidade, equipa simpática. Um pouco caro mas a localização e o resultado compensam.", DateTime.Now.AddDays(-20)),
                    (reviewer3, 5, "Tratamento capilar fenomenal. Cabelo brilhante e hidratado durante semanas. Recomendo!",           DateTime.Now.AddDays(-33))
                });
            }
        }

        // ── Fallback Hardcoded Seed Data ─────────────────────────────────────────

        private static async Task SeedFallbackProvidersAsync(
            ApplicationDbContext context,
            ApplicationUser?     sampleUser,
            ApplicationUser?     reviewer2,
            ApplicationUser?     reviewer3)
        {
            // ── Provider 1: Barbearia Clássica Lisboa ────────────────────────────
            var provider1 = new Barbershop
            {
                Name          = "Barbearia Clássica Lisboa",
                Description   = "Barbearia tradicional no coração de Lisboa. Cortes modernos e clássicos, tratamentos de barba e ambiente premium.",
                Address       = "Rua Augusta 120, 1100-053 Lisboa",
                Latitude      = 38.7100,
                Longitude     = -9.1380,
                PhoneNumber   = "+351 21 000 1111",
                Email         = "classica@barberloc.pt",
                OpeningHours  = "Seg-Sex: 09:00–20:00 | Sáb: 09:00–18:00",
                ImageUrl      = "https://images.unsplash.com/photo-1585747860715-2ba37e788b70?w=800&q=80",
                AverageRating = 4.8,
                PlaceId       = "ChIJBarbershopMockLisboa001",
                GooglePlaceId = "ChIJBarbershopMockLisboa001",
                Rating        = 4.8,
                Category      = BarbershopCategory.Barbershop,
                IsActive      = true,
                CreatedAt     = DateTime.Now.AddMonths(-6),
                UpdatedAt     = DateTime.Now
            };
            context.Barbershops.Add(provider1);
            await context.SaveChangesAsync();

            context.Services.AddRange(new[]
            {
                new Service { BarbershopId = provider1.Id, Name = "Corte de Cabelo",  Description = "Corte moderno ou clássico com acabamento perfeito.", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Male },
                new Service { BarbershopId = provider1.Id, Name = "Aparar Barba",     Description = "Barba tradicional com toalha quente e navalha.",      Price = 10.00m, DurationMinutes = 20, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Male },
                new Service { BarbershopId = provider1.Id, Name = "Corte + Barba",    Description = "Pack completo com desconto especial.",                 Price = 22.00m, DurationMinutes = 45, IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Male }
            });
            await context.SaveChangesAsync();

            // Local reviews for provider 1 (used by GetDetails in Demo Mode)
            await SeedReviewsForShop(context, provider1.Id, new[]
            {
                (sampleUser,  5, "Melhor barbearia de Lisboa! Atendimento impecável e corte perfeito. Voltarei sempre.",           DateTime.Now.AddDays(-5)),
                (reviewer2,   5, "Excelente serviço, preços justos e espaço muito agradável. Recomendo a toda a gente.",           DateTime.Now.AddDays(-12)),
                (reviewer3,   4, "Muito bom corte e atendimento simpático. A única ressalva é a espera, mas vale a pena.",         DateTime.Now.AddDays(-21))
            });

            // ── Provider 2: Salão Elegance Cascais ──────────────────────────────
            var provider2 = new Barbershop
            {
                Name          = "Salão Elegance Cascais",
                Description   = "Cabeleireiro de luxo em Cascais. Especialistas em coloração, penteados e tratamentos capilares para toda a família.",
                Address       = "Av. Marginal 55, 2750-341 Cascais",
                Latitude      = 38.6968,
                Longitude     = -9.4207,
                PhoneNumber   = "+351 21 000 2222",
                Email         = "elegance@barberloc.pt",
                OpeningHours  = "Ter-Sáb: 10:00–19:00 | Dom: 10:00–14:00",
                ImageUrl      = "https://images.unsplash.com/photo-1562322140-8baeececf3df?w=800&q=80",
                AverageRating = 4.6,
                PlaceId       = "ChIJHairSalonMockCascais002",
                GooglePlaceId = "ChIJHairSalonMockCascais002",
                Rating        = 4.6,
                Category      = BarbershopCategory.HairSalon,
                IsActive      = true,
                CreatedAt     = DateTime.Now.AddMonths(-4),
                UpdatedAt     = DateTime.Now
            };
            context.Barbershops.Add(provider2);
            await context.SaveChangesAsync();

            context.Services.AddRange(new[]
            {
                new Service { BarbershopId = provider2.Id, Name = "Corte Feminino",      Description = "Corte e lavagem especializada para senhora.",                  Price = 28.00m, DurationMinutes = 50,  IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Female },
                new Service { BarbershopId = provider2.Id, Name = "Coloração Completa",  Description = "Coloração com produtos premium e acabamento brilhante.",       Price = 65.00m, DurationMinutes = 120, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Female },
                new Service { BarbershopId = provider2.Id, Name = "Corte Masculino",     Description = "Corte e estilização clássica de homem.",                      Price = 18.00m, DurationMinutes = 30,  IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Male }
            });
            await context.SaveChangesAsync();

            // Local reviews for provider 2
            await SeedReviewsForShop(context, provider2.Id, new[]
            {
                (reviewer2,   5, "Fantástico! A coloração ficou exatamente como eu queria. Profissionais de topo.",                DateTime.Now.AddDays(-3)),
                (sampleUser,  4, "Bom serviço e espaço muito elegante. Um pouco caro mas a qualidade justifica.",                 DateTime.Now.AddDays(-18)),
                (reviewer3,   5, "O melhor cabeleireiro de Cascais sem dúvida. Atendimento personalizado e resultado incrível.",  DateTime.Now.AddDays(-30))
            });

            // ── Provider 3: UrbanCuts Porto ──────────────────────────────────────
            var provider3 = new Barbershop
            {
                Name          = "UrbanCuts Porto",
                Description   = "Barbearia moderna no Porto. Cortes urbanos, fade e serviço ao domicílio disponível em toda a cidade.",
                Address       = "Rua de Santa Catarina 300, 4000-447 Porto",
                Latitude      = 41.1496,
                Longitude     = -8.6109,
                PhoneNumber   = "+351 22 000 3333",
                Email         = "urbancutsporto@barberloc.pt",
                OpeningHours  = "Seg-Sáb: 09:00–21:00",
                ImageUrl      = "https://images.unsplash.com/photo-1503951914875-452162b0f3f1?w=800&q=80",
                AverageRating = 4.5,
                PlaceId       = "ChIJBarbershopMockPorto003",
                GooglePlaceId = "ChIJBarbershopMockPorto003",
                Rating        = 4.5,
                Category      = BarbershopCategory.Unisex,
                IsActive      = true,
                CreatedAt     = DateTime.Now.AddMonths(-2),
                UpdatedAt     = DateTime.Now
            };
            context.Barbershops.Add(provider3);
            await context.SaveChangesAsync();

            context.Services.AddRange(new[]
            {
                new Service { BarbershopId = provider3.Id, Name = "Fade / Degradê",      Description = "Fade skin, low, mid ou high com acabamento premium.",         Price = 18.00m, DurationMinutes = 40, IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Male },
                new Service { BarbershopId = provider3.Id, Name = "Corte Unisexo",       Description = "Corte moderno para qualquer género.",                          Price = 20.00m, DurationMinutes = 35, IsAvailable = true, IsMobile = true,  TargetGender = TargetGender.Unisex },
                new Service { BarbershopId = provider3.Id, Name = "Tratamento de Barba", Description = "Hidratação, aparar e modelação completa.",                    Price = 12.00m, DurationMinutes = 25, IsAvailable = true, IsMobile = false, TargetGender = TargetGender.Male }
            });
            await context.SaveChangesAsync();

            // Local reviews for provider 3
            await SeedReviewsForShop(context, provider3.Id, new[]
            {
                (reviewer3,   5, "O fade ficou perfeito! Nunca tinha visto um trabalho tão cuidado. Voltei na semana seguinte.",   DateTime.Now.AddDays(-2)),
                (sampleUser,  4, "Excelente barbearia, ambiente moderno e staff muito simpático. Recomendo o corte unisexo.",      DateTime.Now.AddDays(-9)),
                (reviewer2,   5, "Serviço de domicílio fantástico, pontual e com um resultado profissional. 5 estrelas bem dadas.", DateTime.Now.AddDays(-25))
            });

            // ── Sample Bookings ──────────────────────────────────────────────────
            if (sampleUser != null && !await context.Bookings.AnyAsync())
            {
                var service1 = await context.Services
                    .FirstOrDefaultAsync(s => s.BarbershopId == provider1.Id && s.Name == "Corte de Cabelo");
                var service3 = await context.Services
                    .FirstOrDefaultAsync(s => s.BarbershopId == provider3.Id && s.Name == "Fade / Degradê");

                context.Bookings.Add(new Booking
                {
                    UserId       = sampleUser.Id,
                    BarbershopId = provider1.Id,
                    ServiceId    = service1?.Id,
                    BookingDate  = DateTime.Today.AddDays(3),
                    BookingTime  = new TimeSpan(10, 30, 0),
                    Status       = BookingStatus.Confirmed,
                    Notes        = "Preferência por corte clássico com produto.",
                    IsOnSite     = false,
                    CreatedAt    = DateTime.Now.AddDays(-1)
                });

                context.Bookings.Add(new Booking
                {
                    UserId           = sampleUser.Id,
                    BarbershopId     = provider3.Id,
                    ServiceId        = service3?.Id,
                    BookingDate      = DateTime.Today.AddDays(7),
                    BookingTime      = new TimeSpan(14, 0, 0),
                    Status           = BookingStatus.Pending,
                    Notes            = "Serviço ao domicílio — apartamento no 3º andar.",
                    IsOnSite         = true,
                    TravelDistanceKm = 5.2,
                    TravelFee        = 8.90m,
                    CreatedAt        = DateTime.Now
                });

                await context.SaveChangesAsync();
            }
        }

        // ── Review helper ────────────────────────────────────────────────────────

        /// <summary>
        /// Seeds <see cref="Review"/> records for a barbershop, skipping if reviews already exist.
        /// </summary>
        private static async Task SeedReviewsForShop(
            ApplicationDbContext context,
            int barbershopId,
            IEnumerable<(ApplicationUser? user, int rating, string comment, DateTime createdAt)> entries)
        {
            if (await context.Reviews.AnyAsync(r => r.BarbershopId == barbershopId)) return;

            foreach (var (user, rating, comment, createdAt) in entries)
            {
                if (user == null) continue;
                context.Reviews.Add(new Review
                {
                    UserId       = user.Id,
                    BarbershopId = barbershopId,
                    Rating       = rating,
                    Comment      = comment,
                    CreatedAt    = createdAt
                });
            }

            await context.SaveChangesAsync();
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