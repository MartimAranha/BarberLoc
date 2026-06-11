using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ── MVC + caching ──────────────────────────────────────────────────
            builder.Services.AddControllersWithViews();
            builder.Services.AddMemoryCache();

            // ── Configuration Binding ──────────────────────────────────────────
            builder.Services.Configure<GoogleMapsOptions>(builder.Configuration.GetSection("Google"));

            // ── Google Places service (typed HttpClient) ───────────────────────
            builder.Services.AddHttpClient<GooglePlacesService>();
            builder.Services.AddScoped<IGooglePlacesService, GooglePlacesService>();

            // ── EF Core / SQL Server ───────────────────────────────────────────
            // EnableRetryOnFailure handles transient errors:
            //   • LocalDB named-pipe not yet open at startup (auto-start latency ~200–500 ms)
            //   • Brief network blips in full SQL Server configurations
            //   • Idle connection pool recycling
            // CommandTimeout raised to 60 s for migration runs that may generate large schemas.
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection"),
                    sqlOptions =>
                    {
                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount       : 5,
                            maxRetryDelay       : TimeSpan.FromSeconds(10),
                            errorNumbersToAdd   : null   // null = use EF Core's built-in transient error list
                        );
                        sqlOptions.CommandTimeout(60);
                    }
                )
            );

            // ── ASP.NET Core Identity ──────────────────────────────────────────
            builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount  = false;
                options.Password.RequireDigit           = true;
                options.Password.RequireLowercase       = true;
                options.Password.RequireUppercase       = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength         = 6;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

            // ── External authentication (Google OAuth) ─────────────────────────
            builder.Services.AddAuthentication()
                .AddGoogle(options =>
                {
                    options.ClientId     = builder.Configuration["Authentication:Google:ClientId"]     ?? string.Empty;
                    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
                });

            var app = builder.Build();

            // ── Database migration + seeding ───────────────────────────────────
            // Wrapped in a dedicated connectivity check so a DB outage does NOT
            // prevent the web process from starting — the app serves pages while
            // the DB comes back online (retry policy handles reconnection).
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger   = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    var context = services.GetRequiredService<ApplicationDbContext>();

                    // ── Connectivity pre-check ─────────────────────────────────
                    // CanConnectAsync respects EnableRetryOnFailure, so it will
                    // attempt up to 5 times before throwing. This avoids crashing
                    // MigrateAsync on a hard connection failure.
                    var canConnect = await context.Database.CanConnectAsync();
                    if (!canConnect)
                    {
                        logger.LogError(
                            "Startup DB check failed: cannot reach '{Server}'. " +
                            "Verify that LocalDB (sqllocaldb start MSSQLLocalDB) or your SQL Server instance is running. " +
                            "The application will start but database features will be unavailable.",
                            context.Database.GetConnectionString());
                    }
                    else
                    {
                        // Apply any pending EF Core migrations automatically
                        await context.Database.MigrateAsync();

                        var userManager  = services.GetRequiredService<UserManager<ApplicationUser>>();
                        var roleManager  = services.GetRequiredService<RoleManager<IdentityRole>>();
                        var config       = services.GetRequiredService<IConfiguration>();
                        var googleService = services.GetService<IGooglePlacesService>();

                        await DbSeeder.SeedAsync(config, context, userManager, roleManager, googleService);

                        logger.LogInformation("Database migration and seeding completed successfully.");
                    }
                }
                catch (Exception ex)
                {
                    var logger2 = services.GetRequiredService<ILogger<Program>>();
                    logger2.LogError(ex,
                        "An error occurred during startup database migration/seeding. " +
                        "The application will continue — database features may be degraded.");
                }
            }

            // ── HTTP pipeline ──────────────────────────────────────────────────
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRazorPages();
            app.MapControllerRoute(
                name    : "default",
                pattern : "{controller=Home}/{action=Index}/{id?}");

            await app.RunAsync();
        }
    }
}
