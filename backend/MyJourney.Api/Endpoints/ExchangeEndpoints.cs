using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Services;

namespace MyJourney.Api.Endpoints;

/// <summary>
/// Austausch mit KI-Assistenten ohne API-Zugriff (z. B. ChatGPT im Browser):
/// Bestand als Datei exportieren, Reise-Datei nach Schema "myjourney-trip" importieren.
/// </summary>
public static class ExchangeEndpoints
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IEndpointRouteBuilder MapExchangeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exchange").WithTags("Austausch");

        group.MapGet("/export", async (JourneyDbContext db) =>
        {
            var places = await db.Places.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            var trips = await db.Trips.AsNoTracking()
                .Include(t => t.Stops).ThenInclude(s => s.Place)
                .OrderBy(t => t.Name)
                .ToListAsync();

            var export = new
            {
                schema = "myjourney-bestand",
                schemaVersion = 1,
                exportedAt = DateTime.UtcNow,
                places = places.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    kind = p.Kind,
                    status = p.Status,
                    region = p.Region,
                    country = p.Country,
                    latitude = p.Latitude,
                    longitude = p.Longitude,
                    rating = p.Rating,
                    visitedAt = p.VisitedAt,
                    isStopoverCandidate = p.IsStopoverCandidate,
                    overnight = p.Overnight,
                    headingDeg = p.HeadingDeg,
                    notes = p.Notes,
                }),
                trips = trips.Select(TripResponse.From),
            };

            var json = JsonSerializer.Serialize(export, ExportJson);
            return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "myjourney-bestand.json");
        })
        .WithName("ExportForAssistant")
        .WithSummary("Bestand als JSON-Datei exportieren")
        .WithDescription("Lädt alle Orte und Reisen als eine JSON-Datei herunter – gedacht als Kontext für KI-Assistenten (z. B. ChatGPT), die damit eine neue Reise im Format \"myjourney-trip\" planen können.");

        group.MapGet("/trip-schema", () =>
            Results.Text(TripImportService.SchemaJson, "application/json", Encoding.UTF8))
        .WithName("GetTripImportSchema")
        .WithSummary("JSON Schema für den Reise-Import")
        .WithDescription("Das Schema \"myjourney-trip\" (Version 1), nach dem ein KI-Assistent eine importierbare Reise-Datei erzeugen kann.");

        group.MapGet("/chatgpt-prompt", () =>
            Results.Text(BuildPrompt(), "text/plain", Encoding.UTF8))
        .WithName("GetChatGptPrompt")
        .WithSummary("Prompt-Text für ChatGPT")
        .WithDescription("Ein fertiger Anweisungstext (inkl. JSON Schema), den man ChatGPT zusammen mit dem Bestands-Export mitgeben kann, damit die geplante Reise als importierbare Datei herauskommt.");

        app.MapPost("/api/import/trip", async (JourneyDbContext db, TripImportFile? file) =>
        {
            var errors = TripImportService.Validate(file);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var (result, importErrors) = await TripImportService.ImportAsync(db, file!);
            return importErrors is not null
                ? Results.ValidationProblem(importErrors)
                : Results.Created($"/api/trips/{result!.Trip.Id}", result);
        })
        .WithTags("Import")
        .WithName("ImportTrip")
        .WithSummary("Reise aus einer myjourney-trip-Datei importieren")
        .WithDescription("Nimmt eine Reise im Schema \"myjourney-trip\" (Version 1) entgegen, z. B. von ChatGPT erzeugt. Stopps werden per placeId oder Namensgleichheit mit erfassten Orten verknüpft; unbekannte Stopps werden als neue Wunschlisten-Orte angelegt, außer saveAsPlace ist false.");

        return app;
    }

    private static string BuildPrompt() => $"""
        Ich plane eine Wohnmobil-Reise und nutze dafür die App MyJourney. Bitte hilf mir bei der Planung.

        Als Datei habe ich dir meinen Bestand mitgegeben (myjourney-bestand.json): "places" sind meine
        erfassten Orte (status "Visited" = schon besucht, "Wishlist" = Wunschziel), "trips" meine
        bisherigen Reisen. Berücksichtige den Bestand bei der Planung – Wunschziele in der Gegend
        einbauen, Besuchtes nicht unbedingt wiederholen.

        Wenn die Planung fertig ist, gib mir die Reise als JSON-Datei zum Download, die exakt dem
        JSON Schema unten entspricht ("myjourney-trip", Version 1). Wichtige Regeln dafür:

        - Die Stopps stehen in Fahrreihenfolge.
        - Verweist ein Stopp auf einen Ort aus meinem Bestand, übernimm dessen "id" als "placeId".
        - Für neue Stopps bitte immer "latitude"/"longitude" (WGS84) angeben, dazu "country" und
          möglichst "region" (auf Deutsch) sowie kurze "notes" (Was ansehen? Warum hier halten?).
        - "overnight" beschreibt die Übernachtung am Stopp: "Stellplatz", "Campingplatz", "Frei"
          (frei stehen), "Parkplatz" (z. B. Autobahn-/Rastplatz) oder "None".
        - Reine Routenpunkte ohne eigenen Wert (z. B. eine Fähre) bekommen "saveAsPlace": false.
        - Gib ausschließlich gültiges JSON ohne Kommentare aus.

        Das JSON Schema:

        {TripImportService.SchemaJson}
        """;
}
