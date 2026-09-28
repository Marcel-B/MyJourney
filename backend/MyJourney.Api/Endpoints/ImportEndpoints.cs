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

            // Duplikate überspringen: Name existiert schon (unabhängig von Groß-/Kleinschreibung).
            var existingNames = (await db.Places.AsNoTracking().Select(p => p.Name).ToListAsync())
                .Select(n => n.ToLowerInvariant())
                .ToHashSet();

            var imported = new List<string>();
            var skipped = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var candidate in candidates)
            {
                var key = candidate.Name.ToLowerInvariant();
                if (!existingNames.Add(key))
                {
                    skipped.Add(candidate.Name);
                    continue;
                }

                db.Places.Add(new Place
                {
                    Id = Guid.NewGuid(),
                    Name = candidate.Name,
                    Kind = PlaceKind.Place,
                    Status = PlaceStatus.Wishlist,
                    Latitude = candidate.Latitude,
                    Longitude = candidate.Longitude,
                    Notes = candidate.Notes,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                imported.Add(candidate.Name);
            }

            await db.SaveChangesAsync();

            return Results.Ok(new ImportResult(candidates.Count, imported.Count, skipped.Count, imported, skipped));
        })
        .DisableAntiforgery()
        .WithTags("Import")
        .WithSummary("Google-Maps-Orte importieren")
        .WithDescription("Nimmt eine Google-Takeout-Datei entgegen (\"Gespeicherte Orte\"-GeoJSON oder Listen-CSV) und legt die Orte als Wunschziele an. Bereits vorhandene Namen werden übersprungen.");

        return app;
    }
}
