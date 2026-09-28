using MyJourney.Api.Services;

namespace MyJourney.Api.Endpoints;

public static class NearbyEndpoints
{
    public static IEndpointRouteBuilder MapNearbyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/nearby", async (double lat, double lon, int? radius, OverpassClient overpass, ILogger<OverpassClient> logger, CancellationToken ct) =>
        {
            if (lat is < -90 or > 90 || lon is < -180 or > 180)
                return Results.BadRequest(new { error = "Ungültige Koordinaten." });

            var radiusMeters = Math.Clamp(radius ?? 400, 50, 2000);
            try
            {
                var pois = await overpass.FindNearbyAsync(lat, lon, radiusMeters, ct);
                return Results.Ok(pois);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // OpenStreetMap nicht erreichbar: leere Liste statt Fehler, der Nutzer kann frei benennen.
                logger.LogWarning(ex, "Overpass-Abfrage fehlgeschlagen");
                return Results.Ok(new List<MyJourney.Api.Contracts.NearbyPoi>());
            }
        })
        .WithName("FindNearbyPois")
        .WithTags("Places")
        .WithSummary("Benannte Orte der Umgebung (OpenStreetMap)")
        .WithDescription("Liefert POIs rund um die Koordinaten (Sehenswürdigkeiten, Camping, Gastronomie), sortiert nach Entfernung. Für die \"Hier bin ich\"-Funktion.");

        return app;
    }
}
