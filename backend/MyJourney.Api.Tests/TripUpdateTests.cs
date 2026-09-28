using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Endpoints;
using MyJourney.Api.Models;

namespace MyJourney.Api.Tests;

public class TripUpdateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JourneyDbContext> _options;

    public TripUpdateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<JourneyDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new JourneyDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task UpdateTrip_ErsetztStopps_OhneConcurrencyFehler()
    {
        Guid tripId;
        using (var db = new JourneyDbContext(_options))
        {
            var (trip, error) = await TripEndpoints.BuildTrip(db, new CreateTripRequest(
                "Norwegen",
                Stops: [new TripStopInput(Name: "Kristiansand"), new TripStopInput(Name: "Bergen")]), existing: null);
            Assert.Null(error);
            db.Trips.Add(trip!);
            await db.SaveChangesAsync();
            tripId = trip!.Id;
        }

        // Stopps in neuer Reihenfolge ersetzen – wie es der PUT-Endpoint tut.
        using (var db = new JourneyDbContext(_options))
        {
            var existing = await db.Trips.Include(t => t.Stops).FirstAsync(t => t.Id == tripId);
            var (updatedTrip, error) = await TripEndpoints.BuildTrip(db, new CreateTripRequest(
                "Norwegen",
                Stops:
                [
                    new TripStopInput(Name: "Bergen"),
                    new TripStopInput(Name: "Kristiansand"),
                    new TripStopInput(Name: "Trondheim"),
                ]), existing);
            Assert.Null(error);
            Assert.NotNull(updatedTrip);
            await db.SaveChangesAsync();
        }

        using (var db = new JourneyDbContext(_options))
        {
            var trip = await db.Trips.AsNoTracking().Include(t => t.Stops).FirstAsync(t => t.Id == tripId);
            var ordered = trip.Stops.OrderBy(s => s.Order).Select(s => s.Name).ToList();
            Assert.Equal(["Bergen", "Kristiansand", "Trondheim"], ordered);
            Assert.Equal(3, await db.TripStops.CountAsync());
        }
    }
}
