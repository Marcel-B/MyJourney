using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Models;

namespace MyJourney.Api.Endpoints;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trips").WithTags("Trips");

        group.MapGet("/", async (JourneyDbContext db) =>
        {
            var trips = await db.Trips.AsNoTracking()
                .Include(t => t.Stops)
                .ThenInclude(s => s.Place)
                .OrderByDescending(t => t.UpdatedAt)
                .ToListAsync();
            return Results.Ok(trips.Select(TripResponse.From).ToList());
        })
        .WithSummary("Alle Reisen mit ihren Stopps auflisten");

        group.MapGet("/{id:guid}", async (JourneyDbContext db, Guid id) =>
            await LoadTrip(db, id) is { } trip
                ? Results.Ok(TripResponse.From(trip))
                : Results.NotFound())
        .WithSummary("Eine Reise abrufen");

        group.MapPost("/", async (JourneyDbContext db, CreateTripRequest request) =>
        {
            var (trip, error) = await BuildTrip(db, request, existing: null);
            if (error is not null) return error;

            db.Trips.Add(trip!);
            await db.SaveChangesAsync();

            var created = await LoadTrip(db, trip!.Id);
            return Results.Created($"/api/trips/{trip.Id}", TripResponse.From(created!));
        })
        .WithSummary("Reise anlegen")
        .WithDescription("Legt eine Reise mit geordneten Stopps an. Ein Stopp verweist per placeId auf einen erfassten Ort oder bringt einen eigenen Namen und optionale Koordinaten mit.");

        group.MapPut("/{id:guid}", async (JourneyDbContext db, Guid id, CreateTripRequest request) =>
        {
            var existing = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id);
            if (existing is null) return Results.NotFound();

            var (_, error) = await BuildTrip(db, request, existing);
            if (error is not null) return error;

            await db.SaveChangesAsync();

            var updated = await LoadTrip(db, id);
            return Results.Ok(TripResponse.From(updated!));
        })
        .WithSummary("Reise aktualisieren (ersetzt auch die Stopps)");

        group.MapDelete("/{id:guid}", async (JourneyDbContext db, Guid id) =>
        {
            var deleted = await db.Trips.Where(t => t.Id == id).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Reise löschen");

        return app;
    }

    private static async Task<Trip?> LoadTrip(JourneyDbContext db, Guid id) =>
        await db.Trips.AsNoTracking()
            .Include(t => t.Stops)
            .ThenInclude(s => s.Place)
            .FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>
    /// Validiert die Anfrage und befüllt entweder eine neue Reise oder die übergebene bestehende.
    /// Gibt bei Validierungsfehlern ein Problem-Result zurück.
    /// </summary>
    internal static async Task<(Trip? Trip, IResult? Error)> BuildTrip(
        JourneyDbContext db, CreateTripRequest request, Trip? existing)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Der Name darf nicht leer sein."],
            }));
        }

        var stops = request.Stops ?? [];
        var placeIds = stops.Where(s => s.PlaceId is not null).Select(s => s.PlaceId!.Value).Distinct().ToList();
        var knownPlaces = await db.Places.Where(p => placeIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        var missing = placeIds.Except(knownPlaces).ToList();
        if (missing.Count > 0)
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["stops"] = [$"Unbekannte placeId(s): {string.Join(", ", missing)}"],
            }));
        }

        var invalidStop = stops.FirstOrDefault(s => s.PlaceId is null && string.IsNullOrWhiteSpace(s.Name));
        if (invalidStop is not null)
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["stops"] = ["Jeder Stopp braucht eine placeId oder einen Namen."],
            }));
        }

        var now = DateTime.UtcNow;
        var trip = existing ?? new Trip { Id = Guid.NewGuid(), Name = request.Name.Trim(), CreatedAt = now };

        trip.Name = request.Name.Trim();
        trip.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        trip.StartDate = request.StartDate;
        trip.EndDate = request.EndDate;
        trip.UpdatedAt = now;
        trip.Stops.Clear();
        trip.Stops.AddRange(stops.Select((s, index) => new TripStop
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            Order = index + 1,
            PlaceId = s.PlaceId,
            Name = string.IsNullOrWhiteSpace(s.Name) ? null : s.Name.Trim(),
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim(),
        }));

        return (trip, null);
    }
}
