using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace BarberLoc.Tests
{
    public class AccountControllerTests : IntegrationTestBase
    {
        public AccountControllerTests(CustomWebApplicationFactory<Program> factory) : base(factory)
        {
        }

        [Fact]
        public async Task Get_AdminDiagnostics_ReturnsForbidden_WhenNotAdmin()
        {
            // Act: Using default TestAuthHandler which assigns Role "User", not "Admin"
            var response = await _client.GetAsync("/admin/diagnostics");

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Identity/Account/AccessDenied", response.Headers.Location?.OriginalString);
        }

        [Fact]
        public async Task Get_Bookings_ReturnsSuccess_WhenAuthenticatedAsUser()
        {
            // Arrange
            // Default _client is already configured with TestAuthHandler (User role) in base class
            
            // Act
            var response = await _client.GetAsync("/Bookings");

            // Assert
            response.EnsureSuccessStatusCode(); // 200 OK
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Minhas Reservas", content);
        }
    }
}
