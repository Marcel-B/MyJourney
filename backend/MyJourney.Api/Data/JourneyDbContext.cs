using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Models;

namespace MyJourney.Api.Data;

public class JourneyDbContext(DbContextOptions<JourneyDbContext> options) : DbContext(options)
{
    public DbSet<Place> Places => Set<Place>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<TripStop> TripStops => Set<TripStop>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<Invite> Invites => Set<Invite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Place>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Region).HasMaxLength(200);
            entity.Property(p => p.Country).HasMaxLength(100);
            entity.Property(p => p.Notes).HasMaxLength(4000);
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => p.Kind);
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(200);
            entity.Property(t => t.Notes).HasMaxLength(4000);
            entity.HasMany(t => t.Stops)
                .WithOne()
                .HasForeignKey(s => s.TripId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.Property(u => u.Username).HasMaxLength(100);
            entity.Property(u => u.PasswordHash).HasMaxLength(500);
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<Invite>(entity =>
        {
            entity.Property(i => i.Token).HasMaxLength(100);
            entity.Property(i => i.UsedByUsername).HasMaxLength(100);
            entity.HasIndex(i => i.Token).IsUnique();
        });

        modelBuilder.Entity<TripStop>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Notes).HasMaxLength(4000);
            entity.HasOne(s => s.Place)
                .WithMany()
                .HasForeignKey(s => s.PlaceId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(s => new { s.TripId, s.Order });
        });
    }
}
