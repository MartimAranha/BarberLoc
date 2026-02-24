using Microsoft.AspNetCore.Identity;
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

            // No barbershop seeding - only real barbershops should exist in the database
        }
    }
}
