using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Models;

namespace MyJourney.Api.Endpoints;

public static class PlaceEndpoints
{
    public static IEndpointRouteBuilder MapPlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/places").WithTags("Places");

        group.MapGet("/", async (
            JourneyDbContext db,
            PlaceStatus? status,
            PlaceKind? kind,
            string? search,
            string? region,
            bool? stopoversOnly) =>
        {
            var query = db.Places.AsNoTracking();

            if (status is not null) query = query.Where(p => p.Status == status);
            if (kind is not null) query = query.Where(p => p.Kind == kind);
            if (stopoversOnly == true) query = query.Where(p => p.IsStopoverCandidate);
            if (!string.IsNullOrWhiteSpace(region))
                query = query.Where(p => p.Region != null && EF.Functions.Like(p.Region, $"%{region}%"));
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(p =>
                    EF.Functions.Like(p.Name, $"%{search}%") ||
                    (p.Region != null && EF.Functions.Like(p.Region, $"%{search}%")) ||
                    (p.Country != null && EF.Functions.Like(p.Country, $"%{search}%")) ||
                    (p.Notes != null && EF.Functions.Like(p.Notes, $"%{search}%")));

            var places = await query
                .OrderBy(p => p.Status)
                .ThenBy(p => p.Name)
                .Select(p => PlaceResponse.From(p))
                .ToListAsync();

            return Results.Ok(places);
        })
        .WithSummary("Orte und Regionen auflisten")
        .WithDescription("Filterbar nach Status (Wishlist/Visited), Art (Place/Region), Region, Zwischenstopp-Eignung und Freitextsuche.");

        group.MapGet("/{id:guid}", async (JourneyDbContext db, Guid id) =>
            await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } place
                ? Results.Ok(PlaceResponse.From(place))
                : Results.NotFound())
        .WithSummary("Einen Eintrag abrufen");

        group.MapPost("/", async (JourneyDbContext db, CreatePlaceRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Der Name darf nicht leer sein."],
                });
            if (request.Rating is < 1 or > 5)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["rating"] = ["Die Bewertung muss zwischen 1 und 5 liegen."],
                });

            var now = DateTime.UtcNow;
            var place = new Place
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Kind = request.Kind,
                Status = request.Status,
                Region = NormalizeOptional(request.Region),
                Country = NormalizeOptional(request.Country),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Notes = NormalizeOptional(request.Notes),
                Rating = request.Rating,
                VisitedAt = request.VisitedAt,
                IsStopoverCandidate = request.IsStopoverCandidate,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Places.Add(place);
            await db.SaveChangesAsync();

            return Results.Created($"/api/places/{place.Id}", PlaceResponse.From(place));
        })
        .WithSummary("Neuen Ort oder neue Region anlegen");

        group.MapPut("/{id:guid}", async (JourneyDbContext db, Guid id, UpdatePlaceRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Der Name darf nicht leer sein."],
                });
            if (request.Rating is < 1 or > 5)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["rating"] = ["Die Bewertung muss zwischen 1 und 5 liegen."],
                });

            var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id);
            if (place is null) return Results.NotFound();

            place.Name = request.Name.Trim();
            place.Kind = request.Kind;
            place.Status = request.Status;
            place.Region = NormalizeOptional(request.Region);
            place.Country = NormalizeOptional(request.Country);
            place.Latitude = request.Latitude;
            place.Longitude = request.Longitude;
            place.Notes = NormalizeOptional(request.Notes);
            place.Rating = request.Rating;
            place.VisitedAt = request.VisitedAt;
            place.IsStopoverCandidate = request.IsStopoverCandidate;
            place.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(PlaceResponse.From(place));
        })
        .WithSummary("Eintrag aktualisieren");

        group.MapPost("/{id:guid}/visit", async (JourneyDbContext db, Guid id, MarkVisitedRequest request) =>
        {
            if (request.Rating is < 1 or > 5)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["rating"] = ["Die Bewertung muss zwischen 1 und 5 liegen."],
                });

            var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id);
            if (place is null) return Results.NotFound();

            place.Status = PlaceStatus.Visited;
            place.VisitedAt = request.VisitedAt ?? DateOnly.FromDateTime(DateTime.UtcNow);
            if (request.Rating is not null) place.Rating = request.Rating;
            if (!string.IsNullOrWhiteSpace(request.Notes)) place.Notes = request.Notes.Trim();
            place.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(PlaceResponse.From(place));
        })
        .WithSummary("Eintrag als besucht markieren");

        group.MapDelete("/{id:guid}", async (JourneyDbContext db, Guid id) =>
        {
            var deleted = await db.Places.Where(p => p.Id == id).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Eintrag löschen");

        app.MapGet("/api/regions", async (JourneyDbContext db) =>
        {
            var regions = await db.Places.AsNoTracking()
                .Where(p => p.Region != null && p.Region != "")
                .Select(p => p.Region!)
                .Distinct()
                .OrderBy(r => r)
                .ToListAsync();
            return Results.Ok(regions);
        })
        .WithTags("Places")
        .WithSummary("Alle erfassten Regionsnamen");

        return app;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
