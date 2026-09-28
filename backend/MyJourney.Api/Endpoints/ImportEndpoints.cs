using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Models;
using MyJourney.Api.Services;

namespace MyJourney.Api.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/import/google", async (JourneyDbContext db, IFormFile file) =>
        {
            if (file.Length == 0)
                return Results.BadRequest(new { error = "Die Datei ist leer." });
            if (file.Length > 20 * 1024 * 1024)
                return Results.BadRequest(new { error = "Die Datei ist größer als 20 MB." });

            string content;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                content = await reader.ReadToEndAsync();
            }

            List<ImportedPlaceCandidate> candidates;
            try
            {
                candidates = GoogleTakeoutParser.Parse(content);
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = $"Datei konnte nicht gelesen werden: {ex.Message}" });
            }

            // Vorhandene Orte nach Name (unabhängig von Groß-/Kleinschreibung) nachschlagen können.
            var byName = new Dictionary<string, Place>();
            foreach (var place in await db.Places.ToListAsync())
            {
                byName.TryAdd(place.Name.ToLowerInvariant(), place);
            }

            var imported = new List<string>();
            var updated = new List<string>();
            var skipped = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var candidate in candidates)
            {
                // Google-Daten sind besuchte Orte: Bewertung oder Datum aus der Datei markieren "besucht".
                var marksVisited = candidate.Rating is not null || candidate.VisitedAt is not null;

                var key = candidate.Name.ToLowerInvariant();
                if (byName.TryGetValue(key, out var existing))
                {
                    // Ein vorhandener Eintrag wird aufgewertet: Wunschort -> besucht,
                    // bzw. ein besuchter Ort ohne Bewertung bekommt seine Bewertung.
                    var upgrades = marksVisited &&
                        (existing.Status == PlaceStatus.Wishlist ||
                         (candidate.Rating is not null && existing.Rating is null));
                    if (upgrades)
                    {
                        existing.Status = PlaceStatus.Visited;
                        existing.Rating ??= candidate.Rating;
                        existing.VisitedAt ??= candidate.VisitedAt;
                        existing.Country ??= candidate.Country;
                        existing.Notes = string.IsNullOrWhiteSpace(existing.Notes)
                            ? candidate.Notes
                            : existing.Notes;
                        existing.UpdatedAt = now;
                        updated.Add(candidate.Name);
                    }
                    else
                    {
                        skipped.Add(candidate.Name);
                    }
                    continue;
                }

                var place = new Place
                {
                    Id = Guid.NewGuid(),
                    Name = candidate.Name,
                    Kind = PlaceKind.Place,
                    Status = marksVisited ? PlaceStatus.Visited : PlaceStatus.Wishlist,
                    Latitude = candidate.Latitude,
                    Longitude = candidate.Longitude,
                    Notes = candidate.Notes,
                    Rating = candidate.Rating,
                    VisitedAt = candidate.VisitedAt,
                    Country = candidate.Country,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Places.Add(place);
                byName[key] = place;
                imported.Add(candidate.Name);
            }

            await db.SaveChangesAsync();

            return Results.Ok(new ImportResult(
                candidates.Count, imported.Count, updated.Count, skipped.Count,
                imported, updated, skipped));
        })
        .DisableAntiforgery()
        .WithTags("Import")
        .WithSummary("Google-Maps-Orte importieren")
        .WithDescription("Nimmt eine Google-Takeout-Datei entgegen (\"Gespeicherte Orte\"-GeoJSON, \"Bewertungen\"-GeoJSON oder Listen-CSV). Google-Orte mit Datum oder Bewertung werden als besucht angelegt (Datum aus der Datei), Listen-CSVs als Wunschziele. Ein vorhandener Eintrag wird durch Bewertung/Datum aufgewertet statt doppelt angelegt; sonst werden vorhandene Namen übersprungen.");

        return app;
    }
}
