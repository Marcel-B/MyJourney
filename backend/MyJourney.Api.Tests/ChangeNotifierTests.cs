using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Data;
using MyJourney.Api.Models;
using MyJourney.Api.Services;

namespace MyJourney.Api.Tests;

public class ChangeNotifierTests
{
    private static ChangeNotifier CreateNotifier() => new(new HttpContextAccessor());

    [Fact]
    public void Notify_ErhoehtVersionUndErreichtAbonnenten()
    {
        var notifier = CreateNotifier();
        var versionVorher = notifier.Version;
        var (_, reader) = notifier.Subscribe();

        notifier.NotifyDataChanged();

        Assert.True(notifier.Version > versionVorher);
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal(notifier.Version, evt!.Version);
        Assert.Null(evt.Origin); // kein HTTP-Request im Spiel, also kein Origin
    }

    [Fact]
    public void Kanal_PuffertNurDasNeuesteEreignis()
    {
        var notifier = CreateNotifier();
        var (_, reader) = notifier.Subscribe();

        notifier.NotifyDataChanged();
        notifier.NotifyDataChanged();
        notifier.NotifyDataChanged();

        Assert.True(reader.TryRead(out var evt));
        Assert.Equal(notifier.Version, evt!.Version);
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void Unsubscribe_BeendetZustellung()
    {
        var notifier = CreateNotifier();
        var (id, reader) = notifier.Subscribe();
        notifier.Unsubscribe(id);

        notifier.NotifyDataChanged();

        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task SaveChanges_SignalisiertBeiReisedaten()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<JourneyDbContext>().UseSqlite(connection).Options;
        var notifier = CreateNotifier();
        var (_, reader) = notifier.Subscribe();

        await using var db = new JourneyDbContext(options, notifier);
        await db.Database.EnsureCreatedAsync();
        Assert.False(reader.TryRead(out _)); // Schema anlegen ist keine Datenänderung

        db.Places.Add(new Place { Id = Guid.NewGuid(), Name = "Lofoten" });
        await db.SaveChangesAsync();

        Assert.True(reader.TryRead(out _));
    }

    [Fact]
    public async Task SaveChanges_IgnoriertBenutzerkontenUndEinladungen()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<JourneyDbContext>().UseSqlite(connection).Options;
        var notifier = CreateNotifier();
        var (_, reader) = notifier.Subscribe();

        await using var db = new JourneyDbContext(options, notifier);
        await db.Database.EnsureCreatedAsync();

        db.Invites.Add(new Invite
        {
            Id = Guid.NewGuid(),
            Token = "abc",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();

        Assert.False(reader.TryRead(out _));
    }
}
