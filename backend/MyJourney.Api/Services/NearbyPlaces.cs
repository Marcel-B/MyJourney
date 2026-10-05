using MyJourney.Api.Models;

namespace MyJourney.Api.Services;

/// <summary>Ein erfasster Ort mit seiner Entfernung zum Bezugspunkt.</summary>
public record PlaceDistanceHit(Place Place, double DistanceKm);

/// <summary>
/// Ein Ort voraus in Fahrtrichtung: mit Peilung vom Bezugspunkt zum Ort und – falls der
/// Ort eine gespeicherte Fahrtrichtung hat – ob sie zur aktuellen Richtung passt
/// (richtige Fahrbahnseite; null = keine Richtung am Ort erfasst).
/// </summary>
public record PlaceAheadHit(Place Place, double DistanceKm, double BearingDeg, bool? SameDirection);

/// <summary>
/// Umkreissuche über die eigenen erfassten Orte: filtert auf einen Radius
/// um einen Bezugspunkt und sortiert aufsteigend nach Entfernung.
/// Gemeinsame Logik für REST (/api/places/nearby) und MCP (find_places_nearby).
/// </summary>
public static class NearbyPlaces
{
    public const double DefaultRadiusKm = 20;
    public const double MaxRadiusKm = 1000;

    /// <summary>Maximale Abweichung der Peilung von der Fahrtrichtung (je Seite), damit ein Ort als "voraus" gilt.</summary>
    public const double DefaultCorridorDeg = 45;
    public const double MaxCorridorDeg = 180;

    /// <summary>
    /// Ab dieser Abweichung zwischen gespeicherter und aktueller Fahrtrichtung gilt der Ort
    /// als Gegenrichtung (falsche Fahrbahnseite).
    /// </summary>
    public const double SameDirectionMaxDeltaDeg = 90;

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

    /// <summary>
    /// Orte voraus in Fahrtrichtung: wie <see cref="Find"/>, aber nur Orte, deren Peilung
    /// höchstens <paramref name="corridorDeg"/> von <paramref name="headingDeg"/> abweicht.
    /// </summary>
    public static List<PlaceAheadHit> FindAhead(
        IEnumerable<Place> places, double lat, double lon, double radiusKm,
        double headingDeg, double corridorDeg = DefaultCorridorDeg)
    {
        var heading = NormalizeHeading(headingDeg);
        return places
            .Where(p => p.Latitude is not null && p.Longitude is not null)
            .Select(p =>
            {
                var distanceKm = Math.Round(OverpassClient.DistanceMeters(lat, lon, p.Latitude!.Value, p.Longitude!.Value) / 1000, 1);
                var bearing = Math.Round(BearingDeg(lat, lon, p.Latitude.Value, p.Longitude.Value));
                bool? sameDirection = p.HeadingDeg is null
                    ? null
                    : Math.Abs(AngleDeltaDeg(p.HeadingDeg.Value, heading)) <= SameDirectionMaxDeltaDeg;
                return new PlaceAheadHit(p, distanceKm, NormalizeHeading(bearing), sameDirection);
            })
            .Where(hit => hit.DistanceKm <= radiusKm &&
                          Math.Abs(AngleDeltaDeg(hit.BearingDeg, heading)) <= corridorDeg)
            .OrderBy(hit => hit.DistanceKm)
            .ThenBy(hit => hit.Place.Name)
            .ToList();
    }

    /// <summary>Anfangspeilung vom Startpunkt zum Zielpunkt in Grad (0–360, 0 = Norden).</summary>
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        var rad = Math.PI / 180;
        var dLon = (lon2 - lon1) * rad;
        var y = Math.Sin(dLon) * Math.Cos(lat2 * rad);
        var x = Math.Cos(lat1 * rad) * Math.Sin(lat2 * rad) -
                Math.Sin(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Cos(dLon);
        return NormalizeHeading(Math.Atan2(y, x) / rad);
    }

    /// <summary>Kleinste Winkeldifferenz zweier Richtungen in Grad (über -180 bis einschließlich 180).</summary>
    public static double AngleDeltaDeg(double a, double b)
    {
        var delta = (a - b) % 360;
        if (delta > 180) delta -= 360;
        if (delta <= -180) delta += 360;
        return delta;
    }

    /// <summary>Richtung auf 0–&lt;360 normieren (360 wird zu 0, negative Werte wandern in den Bereich).</summary>
    public static double NormalizeHeading(double deg)
    {
        var normalized = deg % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
