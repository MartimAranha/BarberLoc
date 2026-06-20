using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using WebApplication1.Data;
using WebApplication1.Models;
using Xunit;

namespace BarberLoc.Tests
{
    public abstract class IntegrationTestBase : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        protected readonly CustomWebApplicationFactory<Program> _factory;
        protected readonly HttpClient _client;

        public IntegrationTestBase(CustomWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Set up test user automatically for all tests by default
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.DefaultScheme);

            // Wipe and recreate in-memory DB to ensure true isolation per test execution
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            // Seed Test User so UserManager.GetUserAsync(User) works
            var testUser = new ApplicationUser
            {
                Id = "test-user-id",
                UserName = "testuser@barberloc.com",
                NormalizedUserName = "TESTUSER@BARBERLOC.COM",
                Email = "testuser@barberloc.com",
                NormalizedEmail = "TESTUSER@BARBERLOC.COM",
                EmailConfirmed = true,
                FullName = "Test User"
            };
            db.Users.Add(testUser);
            db.SaveChanges();
        }

        protected ApplicationDbContext GetDbContext()
        {
            var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }
    }
}
