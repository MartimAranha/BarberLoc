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

            // ── Email Service ──────────────────────────────────────────────────
            // IEmailSender: our own abstraction used by ForgotPassword/ResetPassword pages.
            builder.Services.AddScoped<IEmailSender, EmailSender>();

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

            // ── USB Portability & Security Validation ──────────────────────────
            // If the key is missing (e.g. running from a USB drive on a new machine), halt startup
            // and instruct the developer exactly how to restore the environment securely.
            var apiKey = app.Configuration["Google:ApiKey"];
            var placesApiKey = app.Configuration["Google:PlacesApiKey"];
            
            if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(placesApiKey))
            {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                var errorMsg = @"
================================================================================
CRITICAL STARTUP ERROR: MISSING GOOGLE API KEY
================================================================================
This project enforces strict security and does not store API keys in source control.
Because you are running this project on a new machine or from a USB drive, the 
local user-secrets are missing.

To restore the environment, open your terminal in the project directory and run:

    dotnet user-secrets set ""Google:ApiKey"" ""YOUR_KEY_HERE""

The application will now halt.
================================================================================";
                logger.LogCritical(errorMsg);
                
                app.Run(async context => 
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(@"
                        <!DOCTYPE html>
                        <html lang='en'>
                        <head>
                            <meta charset='utf-8'>
                            <meta name='viewport' content='width=device-width, initial-scale=1'>
                            <title>Configuration Error - BarberLoc</title>
                            <style>
                                body { font-family: system-ui, -apple-system, sans-serif; background-color: #f8f9fa; color: #212529; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
                                .container { background: white; padding: 2.5rem; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); max-width: 600px; width: 100%; border-top: 5px solid #dc3545; }
                                h1 { color: #dc3545; margin-top: 0; font-size: 1.5rem; }
                                pre { background: #212529; color: #f8f9fa; padding: 1rem; border-radius: 4px; overflow-x: auto; font-size: 1rem; }
                                p { line-height: 1.6; }
                            </style>
                        </head>
                        <body>
                            <div class='container'>
                                <h1><svg width='24' height='24' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='vertical-align: text-bottom; margin-right: 8px;'><path d='M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z'></path><line x1='12' y1='9' x2='12' y2='13'></line><line x1='12' y1='17' x2='12.01' y2='17'></line></svg>Critical Configuration Error</h1>
                                <p><strong>Missing Google API Key.</strong></p>
                                <p>This project enforces strict security and does not store API keys in source control. Because you are running this project on a new machine or from a USB drive, the local user-secrets are missing.</p>
                                <p>To restore the environment, open your terminal in the project directory and run the following command:</p>
                                <pre>dotnet user-secrets set ""Google:ApiKey"" ""YOUR_KEY_HERE""</pre>
                                <p style='margin-bottom: 0; color: #6c757d; font-size: 0.9em;'>After running the command, restart the application.</p>
                            </div>
                        </body>
                        </html>
                    ");
                });
                app.Run();
                return;
            }

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

                    // ── Database Schema & Seeding ──────────────────────────────
                    // We remove CanConnectAsync because it returns false/throws when the DB doesn't exist,
                    // thus skipping MigrateAsync() and preventing automated database creation.
                    // We use CreateExecutionStrategy() to ensure transient LocalDB startup errors are handled.
                    var strategy = context.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        logger.LogInformation("Applying migrations and ensuring database is created...");
                        await context.Database.MigrateAsync();

                        var userManager  = services.GetRequiredService<UserManager<ApplicationUser>>();
                        var roleManager  = services.GetRequiredService<RoleManager<IdentityRole>>();
                        var config       = services.GetRequiredService<IConfiguration>();
                        var googleService = services.GetService<IGooglePlacesService>();

                        await DbSeeder.SeedAsync(config, context, userManager, roleManager, googleService);

                        logger.LogInformation("Database migration and seeding completed successfully.");
                    });
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
