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
            // ── Seed Roles ───────────────────────────────────────────────────────────
            if (!await roleManager.RoleExistsAsync("Admin")) await roleManager.CreateAsync(new IdentityRole("Admin"));
            if (!await roleManager.RoleExistsAsync("User"))  await roleManager.CreateAsync(new IdentityRole("User"));

            // ── Seed Admin User ──────────────────────────────────────────────────────
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

            // ── Seed Primary Sample User ─────────────────────────────────────────────
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

            // ── Seed Test User for Integration Tests ─────────────────────────────────
            if (await userManager.FindByIdAsync("test-user-id") == null)
            {
                var testUser = new ApplicationUser
                {
                    Id = "test-user-id",
                    UserName = "testuser@barberloc.pt",
                    Email = "testuser@barberloc.pt",
                    FullName = "Test User",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.Now
                };
                await userManager.CreateAsync(testUser, "Test1234!");
                await userManager.AddToRoleAsync(testUser, "User");
            }

            // ── Purge existing Barbershops ───────────────────────────────────────────
            // Always purge and re-seed so demo data is consistent across restarts.
            if (await context.Barbershops.AnyAsync())
            {
                context.Barbershops.RemoveRange(context.Barbershops);
                await context.SaveChangesAsync();
            }

            // ── Seed Demo Barbershops ────────────────────────────────────────────────
            // Three representative Lisbon-area shops for Demo Mode.
            // Coordinates are accurate real-world Lisbon positions.
            // ImageUrl uses Unsplash direct CDN — no API key required.
            // One shop is flagged IsMobileService = true to exercise the Home Service filter.
            var demoBarbershops = new List<Barbershop>
            {
                new Barbershop
                {
                    Name              = "Barbearia Príncipe Real",
                    Description       = "Barbearia clássica no coração do Príncipe Real, especializada em cortes tradicionais e barbas.",
                    Address           = "Rua da Escola Politécnica 12, 1250-100 Lisboa",
                    Latitude          = 38.7175,
                    Longitude         = -9.1492,
                    GooglePlaceId     = "DEMO_PLACE_PRINCIPE_REAL",
                    PlaceId           = "DEMO_PLACE_PRINCIPE_REAL",
                    Category          = BarbershopCategory.Barbershop,
                    IsActive          = true,
                    IsMobileService   = false,
                    ImageUrl          = "https://images.unsplash.com/photo-1585747860715-2ba37e788b70?w=800&auto=format&fit=crop",
                    OperationalStatus = OperationalStatus.Active,
                    CreatedAt         = DateTime.Now,
                    UpdatedAt         = DateTime.Now
                },
                new Barbershop
                {
                    Name              = "Studio Glamour Belém",
                    Description       = "Cabeleireiro unissexo moderno perto da Torre de Belém, com serviços de coloração e tratamentos capilares.",
                    Address           = "Rua de Belém 48, 1300-085 Lisboa",
                    Latitude          = 38.6969,
                    Longitude         = -9.2059,
                    GooglePlaceId     = "DEMO_PLACE_BELEM_STUDIO",
                    PlaceId           = "DEMO_PLACE_BELEM_STUDIO",
                    Category          = BarbershopCategory.Unisex,
                    IsActive          = true,
                    IsMobileService   = false,
                    ImageUrl          = "https://images.unsplash.com/photo-1521590832167-7bcbfaa6381f?w=800&auto=format&fit=crop",
                    OperationalStatus = OperationalStatus.Active,
                    CreatedAt         = DateTime.Now,
                    UpdatedAt         = DateTime.Now
                },
                new Barbershop
                {
                    Name              = "BarberLoc Mobile — Ao Domicílio",
                    Description       = "Barbeiro profissional que se desloca a qualquer ponto de Lisboa. Reserve online e receba em casa!",
                    Address           = "Lisboa (serviço ao domicílio — zona centro)",
                    Latitude          = 38.7223,
                    Longitude         = -9.1393,
                    GooglePlaceId     = "DEMO_PLACE_MOBILE_BARBER",
                    PlaceId           = "DEMO_PLACE_MOBILE_BARBER",
                    Category          = BarbershopCategory.Barbershop,
                    IsActive          = true,
                    IsMobileService   = true,   // ← Home Service flag
                    ImageUrl          = "https://images.unsplash.com/photo-1599351431202-1e0f0137899a?w=800&auto=format&fit=crop",
                    OperationalStatus = OperationalStatus.Active,
                    CreatedAt         = DateTime.Now,
                    UpdatedAt         = DateTime.Now
                }
            };

            context.Barbershops.AddRange(demoBarbershops);
            await context.SaveChangesAsync();

            // ── Seed Services per demo barbershop ────────────────────────────────────
            // Each shop gets "Corte de Cabelo" + "Barba Completa".
            // For the mobile shop, IsMobile and IsHomeService are both true so the
            // existing BarbershopsController.GetMapData hasMobile check passes.
            var savedShops = await context.Barbershops.ToListAsync();
            var serviceSeeds = new List<Service>();

            foreach (var shop in savedShops)
            {
                serviceSeeds.Add(new Service
                {
                    BarbershopId    = shop.Id,
                    Name            = "Corte de Cabelo",
                    Description     = "Corte clássico com máquina e tesoura.",
                    Price           = 12.00m,
                    DurationMinutes = 30,
                    IsAvailable     = true,
                    IsMobile        = shop.IsMobileService,
                    IsHomeService   = shop.IsMobileService,
                    TargetGender    = TargetGender.Unisex
                });

                serviceSeeds.Add(new Service
                {
                    BarbershopId    = shop.Id,
                    Name            = "Barba Completa",
                    Description     = "Aparar, contornar e hidratação de barba.",
                    Price           = 10.00m,
                    DurationMinutes = 20,
                    IsAvailable     = true,
                    IsMobile        = shop.IsMobileService,
                    IsHomeService   = shop.IsMobileService,
                    TargetGender    = TargetGender.Male
                });
            }

            context.Services.AddRange(serviceSeeds);
            await context.SaveChangesAsync();

            // ── Seed Favorite Barbershops ────────────────────────────────────────────
            var existingSampleUser = await userManager.FindByEmailAsync("joao@example.com");
            if (existingSampleUser != null && !await context.FavoriteBarbershops.AnyAsync())
            {
                var firstShop = savedShops.FirstOrDefault();
                if (firstShop != null)
                {
                    context.FavoriteBarbershops.Add(new FavoriteBarbershop
                    {
                        UserId = existingSampleUser.Id,
                        BarbershopId = firstShop.Id,
                        CreatedAt = DateTime.Now
                    });
                    await context.SaveChangesAsync();
                }
            }

            // ── Seed Demo Bookings ───────────────────────────────────────────────────
            // Seeds three bookings for joao@example.com demonstrating that the system
            // permits multiple reservations regardless of Pending / Cancelled status.
            // Guard: only runs if no bookings exist yet (idempotent across restarts).
            if (existingSampleUser != null && !await context.Bookings.AnyAsync())
            {
                var shops        = await context.Barbershops.ToListAsync();
                var services     = await context.Services.ToListAsync();
                var shop1        = shops.ElementAtOrDefault(0);
                var shop2        = shops.ElementAtOrDefault(1);
                var shop3        = shops.ElementAtOrDefault(2);
                var svc1         = services.FirstOrDefault(s => s.BarbershopId == shop1?.Id);
                var svc2         = services.FirstOrDefault(s => s.BarbershopId == shop2?.Id);

                var bookingSeeds = new List<Booking>();

                // 1. Completed booking – past date, shop 1
                if (shop1 != null)
                {
                    bookingSeeds.Add(new Booking
                    {
                        UserId      = existingSampleUser.Id,
                        BarbershopId = shop1.Id,
                        ServiceId   = svc1?.Id,
                        BookingDate = DateTime.Today.AddDays(-7),
                        BookingTime = new TimeSpan(10, 0, 0),
                        Status      = BookingStatus.Completed,
                        Notes       = "Reserva demo concluída.",
                        IsOnSite    = false,
                        CreatedAt   = DateTime.Now.AddDays(-8)
                    });
                }

                // 2. Cancelled booking – recent past, shop 2
                if (shop2 != null)
                {
                    bookingSeeds.Add(new Booking
                    {
                        UserId       = existingSampleUser.Id,
                        BarbershopId = shop2.Id,
                        ServiceId    = svc2?.Id,
                        BookingDate  = DateTime.Today.AddDays(-3),
                        BookingTime  = new TimeSpan(14, 30, 0),
                        Status       = BookingStatus.Cancelled,
                        Notes        = "Reserva cancelada pelo utilizador.",
                        IsOnSite     = false,
                        CreatedAt    = DateTime.Now.AddDays(-5)
                    });
                }

                // 3. Pending booking – future date, shop 1 (proves multiple Pending allowed)
                if (shop1 != null)
                {
                    bookingSeeds.Add(new Booking
                    {
                        UserId       = existingSampleUser.Id,
                        BarbershopId = shop1.Id,
                        ServiceId    = svc1?.Id,
                        BookingDate  = DateTime.Today.AddDays(3),
                        BookingTime  = new TimeSpan(11, 0, 0),
                        Status       = BookingStatus.Pending,
                        Notes        = "Reserva Uber Barber — aguarda confirmação.",
                        IsOnSite     = false,
                        CreatedAt    = DateTime.Now
                    });
                }

                // 4. Home-service (Domicílio) booking — BarbershopId null, external Google Places proxy
                bookingSeeds.Add(new Booking
                {
                    UserId       = existingSampleUser.Id,
                    BarbershopId = null,   // intentionally null: external/home-service booking
                    ServiceId    = null,
                    BookingDate  = DateTime.Today.AddDays(5),
                    BookingTime  = new TimeSpan(9, 0, 0),
                    Status       = BookingStatus.Pending,
                    Notes        = "[Serviço ao Domicílio (Uber-Style)] Morada: Rua da Glória 5, Lisboa | Local/Profissional: BarberLoc Mobile",
                    IsOnSite     = true,
                    TravelDistanceKm = 3.4,
                    TravelFee    = 7.55m,
                    CreatedAt    = DateTime.Now
                });

                context.Bookings.AddRange(bookingSeeds);
                await context.SaveChangesAsync();
            }
        }
    }
}