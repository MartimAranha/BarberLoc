using System.Net;
using WebApplication1.Models;
using Xunit;

namespace BarberLoc.Tests
{
    public class BarberWorkflowTests : IntegrationTestBase
    {
        public BarberWorkflowTests(CustomWebApplicationFactory<Program> factory) : base(factory)
        {
        }

        [Fact]
        public async Task Post_CreateBooking_SavesToDatabase()
        {
            // Arrange
            var db = GetDbContext();
            var barbershop = new Barbershop 
            { 
                Name = "Test Shop", 
                Address = "123 Test St", 
                IsActive = true 
            };
            db.Barbershops.Add(barbershop);
            await db.SaveChangesAsync();

            var formValues = new Dictionary<string, string>
            {
                { "BarbershopId", barbershop.Id.ToString() },
                { "ShopName", barbershop.Name },
                { "BookingDate", DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") },
                { "BookingTime", "14:30" },
                { "Notes", "Test Booking Note" },
                { "IsOnSite", "false" }
            };

            var content = new FormUrlEncodedContent(formValues);
            
            // Bypass CSRF for tests using our custom Antiforgery configured in Factory
            _client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "test-token");
            _client.DefaultRequestHeaders.Add("Cookie", "Test.Antiforgery=test-token");

            // Act
            var response = await _client.PostAsync($"/Bookings/Create", content);

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); // Redirects to Index on success
            Assert.Equal("/Bookings", response.Headers.Location?.OriginalString);

            var newBooking = db.Bookings.FirstOrDefault();
            Assert.NotNull(newBooking);
            Assert.Equal(barbershop.Id, newBooking.BarbershopId);
            Assert.Equal("Test Booking Note", newBooking.Notes);
            Assert.Equal("test-user-id", newBooking.UserId); // From TestAuthHandler
        }
    }
}
