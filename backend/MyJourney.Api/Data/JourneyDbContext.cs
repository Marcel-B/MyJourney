using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Models;

namespace MyJourney.Api.Data;

public class JourneyDbContext(DbContextOptions<JourneyDbContext> options, Services.ChangeNotifier? notifier = null)
    : DbContext(options)
{
    public DbSet<Place> Places => Set<Place>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<TripStop> TripStops => Set<TripStop>();

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<Invite> Invites => Set<Invite>();

    // Zentrale Stelle für "Daten geändert": jedes Speichern von Reisedaten (egal ob über
    // REST, MCP oder einen Import) signalisiert den verbundenen Clients, neu zu laden.
    // Benutzerkonten und Einladungen zählen bewusst nicht dazu.
    public override int SaveChanges()
    {
        var touchesJourneyData = TouchesJourneyData();
        var result = base.SaveChanges();
        if (result > 0 && touchesJourneyData) notifier?.NotifyDataChanged();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var touchesJourneyData = TouchesJourneyData();
        var result = await base.SaveChangesAsync(cancellationToken);
        if (result > 0 && touchesJourneyData) notifier?.NotifyDataChanged();
        return result;
    }

    private bool TouchesJourneyData() => ChangeTracker.Entries().Any(e =>
        e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
        e.Entity is Place or Trip or TripStop);

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
