using MyJourney.Api.Models;

namespace MyJourney.Api.Services;

/// <summary>Ein erfasster Ort mit seiner Entfernung zum Bezugspunkt.</summary>
public record PlaceDistanceHit(Place Place, double DistanceKm);

/// <summary>
/// Umkreissuche über die eigenen erfassten Orte: filtert auf einen Radius
/// um einen Bezugspunkt und sortiert aufsteigend nach Entfernung.
/// Gemeinsame Logik für REST (/api/places/nearby) und MCP (find_places_nearby).
/// </summary>
public static class NearbyPlaces
{
    public const double DefaultRadiusKm = 20;
    public const double MaxRadiusKm = 1000;

    public static List<PlaceDistanceHit> Find(IEnumerable<Place> places, double lat, double lon, double radiusKm)
    {
        return places
            .Where(p => p.Latitude is not null && p.Longitude is not null)
            .Select(p => new PlaceDistanceHit(
                p,
                Math.Round(OverpassClient.DistanceMeters(lat, lon, p.Latitude!.Value, p.Longitude!.Value) / 1000, 1)))
            .Where(hit => hit.DistanceKm <= radiusKm)
            .OrderBy(hit => hit.DistanceKm)
            .ThenBy(hit => hit.Place.Name)
            .ToList();
    }
}
