using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Models;

namespace MyJourney.Api.Data;

public class JourneyDbContext(DbContextOptions<JourneyDbContext> options) : DbContext(options)
{
    public DbSet<Place> Places => Set<Place>();

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
    }
}
