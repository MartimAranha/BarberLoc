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



            // ── Purge Mock Barbershops ───────────────────────────────────────────
            // Delete all existing mock barbershops from the database so they never appear again.
            if (await context.Barbershops.AnyAsync())
            {
                context.Barbershops.RemoveRange(context.Barbershops);
                await context.SaveChangesAsync();
            }

            // ── Seed Barbershops ─────────────────────────────────────────────────
            // As requested, barbershops are no longer seeded or saved in the database.
            // All barbershop data is fetched dynamically from Google Maps APIs.
        }

    }
}