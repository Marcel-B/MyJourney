using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Models;
using MyJourney.Api.Services;

namespace MyJourney.Api.Tests;

public class TripImportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JourneyDbContext> _options;

    public TripImportServiceTests()
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

    private static TripImportFile ValidFile(params TripImportStop[] stops) => new(
        Schema: "myjourney-trip",
        SchemaVersion: 1,
        Trip: new TripImportTrip(
            Name: "Südnorwegen 2027",
            Notes: "Mit der Fähre ab Hirtshals.",
            StartDate: new DateOnly(2027, 6, 1),
            Stops: [.. stops]));

    [Fact]
    public void Validate_MeldetFehlendeReise()
    {
        var errors = TripImportService.Validate(new TripImportFile(Schema: "myjourney-trip", SchemaVersion: 1));
        Assert.Contains("trip", errors.Keys);
    }

    [Fact]
    public void Validate_MeldetFalscheSchemaVersion()
    {
        var file = ValidFile(new TripImportStop(Name: "Bergen")) with { SchemaVersion = 2 };
        var errors = TripImportService.Validate(file);
        Assert.Contains("schemaVersion", errors.Keys);
    }

    [Fact]
    public void Validate_MeldetFalschesSchema()
    {
        var file = ValidFile(new TripImportStop(Name: "Bergen")) with { Schema = "sonstwas" };
        var errors = TripImportService.Validate(file);
        Assert.Contains("schema", errors.Keys);
    }

    [Fact]
    public void Validate_MeldetStoppOhneNamen()
    {
        var errors = TripImportService.Validate(ValidFile(
            new TripImportStop(Name: "Bergen"),
            new TripImportStop(Notes: "ohne Namen")));
        Assert.Contains("trip.stops", errors.Keys);
        Assert.Contains("Stopp 2", errors["trip.stops"][0]);
    }

    [Fact]
    public void Validate_AkzeptiertGueltigeDatei()
    {
        var errors = TripImportService.Validate(ValidFile(new TripImportStop(Name: "Bergen")));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Import_LegtNeueOrteAlsWunschzieleAn()
    {
        using var db = new JourneyDbContext(_options);
        var (result, errors) = await TripImportService.ImportAsync(db, ValidFile(
            new TripImportStop(
                Name: "Lindesnes",
                Latitude: 57.98,
                Longitude: 7.05,
                Country: "Norwegen",
                Region: "Südnorwegen",
                Overnight: OvernightType.Stellplatz,
                IsStopoverCandidate: true,
                Notes: "Leuchtturm am Südkap")));

        Assert.Null(errors);
        Assert.Equal(["Lindesnes"], result!.CreatedPlaces);

        var place = await db.Places.SingleAsync(p => p.Name == "Lindesnes");
        Assert.Equal(PlaceStatus.Wishlist, place.Status);
        Assert.Equal("Norwegen", place.Country);
        Assert.Equal(OvernightType.Stellplatz, place.Overnight);
        Assert.True(place.IsStopoverCandidate);

        var stop = Assert.Single(result.Trip.Stops);
        Assert.Equal(place.Id, stop.PlaceId);
        Assert.Equal("Leuchtturm am Südkap", stop.Notes);
        Assert.Equal(57.98, stop.Latitude);
    }

    [Fact]
    public async Task Import_VerknuepftVorhandeneOrtePerIdUndName()
    {
        Guid byIdPlace;
        using (var db = new JourneyDbContext(_options))
        {
            db.Places.AddRange(
                new Place { Id = Guid.NewGuid(), Name = "Bergen", Status = PlaceStatus.Wishlist },
                new Place { Id = byIdPlace = Guid.NewGuid(), Name = "Kristiansand", Status = PlaceStatus.Visited });
            await db.SaveChangesAsync();
        }

        using (var db = new JourneyDbContext(_options))
        {
            var (result, errors) = await TripImportService.ImportAsync(db, ValidFile(
                new TripImportStop(Name: "Kristiansand", PlaceId: byIdPlace),
                new TripImportStop(Name: "BERGEN")));

            Assert.Null(errors);
            Assert.Empty(result!.CreatedPlaces);
            Assert.Equal(["Kristiansand", "Bergen"], result.LinkedPlaces);
            Assert.Equal(2, await db.Places.CountAsync());
        }
    }

    [Fact]
    public async Task Import_LaesstFreieRoutenpunkteZu()
    {
        using var db = new JourneyDbContext(_options);
        var (result, errors) = await TripImportService.ImportAsync(db, ValidFile(
            new TripImportStop(Name: "Fähre Hirtshals–Kristiansand", SaveAsPlace: false, Notes: "3h Überfahrt"),
            new TripImportStop(Name: "Kristiansand")));

        Assert.Null(errors);
        Assert.Equal(["Fähre Hirtshals–Kristiansand"], result!.FreeStops);
        Assert.Equal(["Kristiansand"], result.CreatedPlaces);
        Assert.Single(await db.Places.ToListAsync());

        var ferry = result.Trip.Stops[0];
        Assert.Null(ferry.PlaceId);
        Assert.Equal("Fähre Hirtshals–Kristiansand", ferry.Name);
    }

    [Fact]
    public async Task Import_MeldetUnbekanntePlaceId()
    {
        using var db = new JourneyDbContext(_options);
        var (result, errors) = await TripImportService.ImportAsync(db, ValidFile(
            new TripImportStop(Name: "Bergen", PlaceId: Guid.NewGuid())));

        Assert.Null(result);
        Assert.Contains("trip.stops", errors!.Keys);
        Assert.Empty(await db.Trips.ToListAsync());
        Assert.Empty(await db.Places.ToListAsync());
    }

    [Fact]
    public async Task Import_UebernimmtReisedaten()
    {
        using var db = new JourneyDbContext(_options);
        var (result, errors) = await TripImportService.ImportAsync(db, ValidFile(
            new TripImportStop(Name: "Bergen")));

        Assert.Null(errors);
        Assert.Equal("Südnorwegen 2027", result!.Trip.Name);
        Assert.Equal(new DateOnly(2027, 6, 1), result.Trip.StartDate);
        Assert.Equal("Mit der Fähre ab Hirtshals.", result.Trip.Notes);
    }
}
