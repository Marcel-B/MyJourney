using MyJourney.Api.Services;

namespace MyJourney.Api.Endpoints;

public static class GeocodeEndpoints
{
    public static IEndpointRouteBuilder MapGeocodeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/geocode", async (string? q, int? limit, NominatimClient nominatim, ILogger<NominatimClient> logger, CancellationToken ct) =>
        {
            var query = q?.Trim() ?? "";
            if (query.Length < 2)
                return Results.BadRequest(new { error = "Suchtext muss mindestens 2 Zeichen haben." });

            var maxResults = Math.Clamp(limit ?? 8, 1, 10);
            try
            {
                var results = await nominatim.SearchAsync(query, maxResults, ct);
                return Results.Ok(results);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Nominatim nicht erreichbar: leere Liste statt Fehler, der Nutzer kann frei eingeben.
                logger.LogWarning(ex, "Nominatim-Suche fehlgeschlagen");
                return Results.Ok(new List<MyJourney.Api.Contracts.GeocodeResult>());
            }
        })
        .WithName("GeocodeSearch")
        .WithTags("Places")
        .WithSummary("Ortssuche (OpenStreetMap Nominatim)")
        .WithDescription("Sucht Orte nach Name und liefert Treffer mit Koordinaten, Region und Land – für die Ortssuche im Dialog \"Neues Ziel\".");

        return app;
    }
}
