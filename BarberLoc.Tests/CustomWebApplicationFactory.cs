using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApplication1.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BarberLoc.Tests
{
    public class CustomWebApplicationFactory<TProgram>
        : WebApplicationFactory<TProgram> where TProgram : class
    {
        static CustomWebApplicationFactory()
        {
            // Set environment variable before Program.cs is executed by the factory
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"Authentication:Google:ClientId", "dummy-client-id"},
                    {"Authentication:Google:ClientSecret", "dummy-client-secret"},
                    {"Google:ApiKey", "dummy-google-api-key"},
                    {"Google:PlacesApiKey", "dummy-places-api-key"}
                });
            });

            builder.ConfigureTestServices(services =>
            {
                var options = services.Where(s => s.ServiceType.Name.Contains("DbContextOptions")).ToList();
                foreach (var o in options) services.Remove(o);

                // Create a new isolated service provider for EF Core InMemory database.
                // This guarantees that EF Core will not see any SQL Server extensions
                // that may be inadvertently registered in the main application's DI container.
                var internalServiceProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("InMemoryDbForTesting");
                    options.UseInternalServiceProvider(internalServiceProvider);
                });

                // Mock Authentication
                services.AddAuthentication(TestAuthHandler.DefaultScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.DefaultScheme, options => { });
                
                // Mock Antiforgery to bypass CSRF checks
                services.AddSingleton<IAntiforgery, MockAntiforgery>();
            });
        }
    }

    public class MockAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new AntiforgeryTokenSet("test", "test", "formFieldName", "headerName");
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => new AntiforgeryTokenSet("test", "test", "formFieldName", "headerName");
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
    }
}
