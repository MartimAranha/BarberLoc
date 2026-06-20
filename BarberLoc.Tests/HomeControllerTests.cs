using System.Net;
using Xunit;

namespace BarberLoc.Tests
{
    public class HomeControllerTests : IntegrationTestBase
    {
        public HomeControllerTests(CustomWebApplicationFactory<Program> factory) : base(factory)
        {
            // Remove auth header for public page tests
            _client.DefaultRequestHeaders.Authorization = null;
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/Home/Privacy")]
        [InlineData("/Barbershops")]
        public async Task Get_EndpointsReturnSuccessAndCorrectContentType(string url)
        {
            // Act
            var response = await _client.GetAsync(url);

            // Assert
            response.EnsureSuccessStatusCode(); // Status Code 200-299
            Assert.Equal("text/html; charset=utf-8", 
                response.Content.Headers.ContentType?.ToString());
        }

        [Fact]
        public async Task Get_ProtectedEndpoint_RedirectsToLogin_WhenUnauthenticated()
        {
            // Act
            var response = await _client.GetAsync("/Bookings");

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new Exception($"500 Error: {content}");
            }

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Identity/Account/Login", response.Headers.Location?.OriginalString);
        }
    }
}
