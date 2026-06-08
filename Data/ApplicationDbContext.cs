using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        
        public DbSet<Barbershop> Barbershops { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<CachedGoogleReview> CachedGoogleReviews { get; set; }
        public DbSet<FavouritePlace> FavouritePlaces { get; set; }
        
        /// <summary>Cache table for barbershop data sourced from the Google Places API.</summary>
        public DbSet<BarberShopPlace> BarberShopPlaces { get; set; }
        
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            
            // Configure relationships and constraints
            builder.Entity<Review>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);
                
            builder.Entity<Review>()
                .HasOne(r => r.Barbershop)
                .WithMany(b => b.Reviews)
                .HasForeignKey(r => r.BarbershopId)
                .OnDelete(DeleteBehavior.Cascade);
                
            builder.Entity<Booking>()
                .HasOne(b => b.User)
                .WithMany(u => u.Bookings)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Restrict);
                
            builder.Entity<Booking>()
                .HasOne(b => b.Barbershop)
                .WithMany(bs => bs.Bookings)
                .HasForeignKey(b => b.BarbershopId)
                .OnDelete(DeleteBehavior.Cascade);
                
            builder.Entity<Service>()
                .HasOne(s => s.Barbershop)
                .WithMany(b => b.Services)
                .HasForeignKey(s => s.BarbershopId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CachedGoogleReview>()
                .HasIndex(c => c.PlaceId)
                .IsUnique();

            // FavouritePlace → ApplicationUser (string PK from Identity)
            builder.Entity<FavouritePlace>()
                .HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Prevent duplicate favourites per user
            builder.Entity<FavouritePlace>()
                .HasIndex(f => new { f.UserId, f.PlaceId })
                .IsUnique();

            // BarberShopPlace — unique PlaceId (one cache row per Google place)
            builder.Entity<BarberShopPlace>()
                .HasIndex(b => b.PlaceId)
                .IsUnique();
        }
    }
}
