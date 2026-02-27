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

            // Seed Barbershops and Services
            if (!context.Barbershops.AsNoTracking().Any())
            {
                var barbershop = new Barbershop
                {
                    Name = "Barbearia Central",
                    Description = "A melhor barbearia no centro da cidade. Estilo clássico e moderno.",
                    Address = "Rua Augusta 123, Lisboa",
                    Latitude = 38.7139,
                    Longitude = -9.1394,
                    PhoneNumber = "210000000",
                    Email = "geral@barbeariacentral.pt",
                    OpeningHours = "Seg-Sáb: 09:00 - 19:00",
                    ImageUrl = "https://images.unsplash.com/photo-1585747860715-2ba37e788b70?w=800",
                    Category = BarbershopCategory.Barbershop,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                context.Barbershops.Add(barbershop);
                await context.SaveChangesAsync();

                // Add Services for this barbershop
                var services = new List<Service>
                {
                    new Service { Name = "Corte de Cabelo", Description = "Corte clássico ou moderno", Price = 15.00m, DurationMinutes = 30, IsAvailable = true, BarbershopId = barbershop.Id },
                    new Service { Name = "Barba", Description = "Aparar e delinear a barba", Price = 10.00m, DurationMinutes = 20, IsAvailable = true, BarbershopId = barbershop.Id },
                    new Service { Name = "Corte e Barba", Description = "Pack completo", Price = 22.00m, DurationMinutes = 50, IsAvailable = true, BarbershopId = barbershop.Id }
                };

                context.Services.AddRange(services);
                
                var barbershop2 = new Barbershop
                {
                    Name = "Vogue Hair Studio",
                    Description = "Salão unisexo com especialistas em coloração.",
                    Address = "Avenida da Liberdade 456, Lisboa",
                    Latitude = 38.7223,
                    Longitude = -9.1450,
                    PhoneNumber = "211111111",
                    Email = "vogue@example.com",
                    OpeningHours = "Ter-Sáb: 10:00 - 20:00",
                    ImageUrl = "https://images.unsplash.com/photo-1560066984-138dadb4c035?w=800",
                    Category = BarbershopCategory.Unisex,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                context.Barbershops.Add(barbershop2);
                await context.SaveChangesAsync();

                context.Services.AddRange(new List<Service>
                {
                    new Service { Name = "Corte Feminino", Description = "Corte e lavagem", Price = 25.00m, DurationMinutes = 45, IsAvailable = true, BarbershopId = barbershop2.Id },
                    new Service { Name = "Coloração", Description = "Coloração profissional", Price = 40.00m, DurationMinutes = 90, IsAvailable = true, BarbershopId = barbershop2.Id }
                });

                await context.SaveChangesAsync();
            }
        }
    }
}
