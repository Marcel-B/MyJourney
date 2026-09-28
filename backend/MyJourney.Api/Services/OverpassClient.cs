using System.Text.Json;
using MyJourney.Api.Contracts;

namespace MyJourney.Api.Services;

/// <summary>
/// Fragt benannte POIs der Umgebung bei OpenStreetMap (Overpass API) ab –
/// Sehenswürdigkeiten, Camping-/Stellplätze, Gastronomie und Ähnliches.
/// </summary>
public class OverpassClient(HttpClient http)
{
    // Öffentliche Overpass-Instanzen; wenn die erste drosselt oder lahmt, hilft der Mirror.
    private static readonly string[] Endpoints =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
    ];

    public async Task<List<NearbyPoi>> FindNearbyAsync(double lat, double lon, int radiusMeters, CancellationToken ct)
    {
        var around = $"(around:{radiusMeters},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        // Kategorien, die für einen Reiseplaner interessant sind; "name" ist Pflicht.
        var query = $$"""
            [out:json][timeout:8];
            (
              nwr{{around}}[name][tourism];
              nwr{{around}}[name][historic];
              nwr{{around}}[name][leisure~"^(park|beach_resort|marina|nature_reserve|garden)$"];
              nwr{{around}}[name][amenity~"^(restaurant|cafe|bar|pub|biergarten|fast_food|marketplace|theatre|cinema|place_of_worship)$"];
            );
            out center 40;
            """;

        Exception? lastError = null;
        foreach (var endpoint in Endpoints)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                using var response = await http.PostAsync(endpoint,
                    new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]), attempt.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(attempt.Token);
                var pois = ParseResponse(json, lat, lon);
                // Leere Liste mit "remark" heißt meist: Server überlastet -> nächsten probieren.
                if (pois.Count > 0 || !json.Contains("\"remark\"")) return pois;
                lastError = new HttpRequestException($"Overpass-Remark von {endpoint}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                lastError = ex;
            }
        }

        throw lastError as HttpRequestException
            ?? new HttpRequestException("Keine Overpass-Instanz erreichbar.", lastError);
    }

    /// <summary>Overpass-Antwort in POIs übersetzen: Name, Koordinaten, Kategorie, Distanz.</summary>
    internal static List<NearbyPoi> ParseResponse(string json, double originLat, double originLon)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var elements) ||
            elements.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<NearbyPoi>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) continue;
            var name = GetString(tags, "name");
            if (string.IsNullOrWhiteSpace(name) || !seenNames.Add(name.Trim())) continue;

            // Nodes tragen lat/lon direkt, Ways/Relations über "center".
            double? lat = GetDouble(element, "lat"), lon = GetDouble(element, "lon");
            if (lat is null && element.TryGetProperty("center", out var center))
            {
                lat = GetDouble(center, "lat");
                lon = GetDouble(center, "lon");
            }
            if (lat is null || lon is null) continue;

            var category = GetString(tags, "tourism") ?? GetString(tags, "historic")
                ?? GetString(tags, "leisure") ?? GetString(tags, "amenity") ?? "poi";

            result.Add(new NearbyPoi(
                name.Trim(), lat.Value, lon.Value, category,
                (int)Math.Round(DistanceMeters(originLat, originLon, lat.Value, lon.Value))));
        }

        return result.OrderBy(p => p.DistanceMeters).Take(15).ToList();
    }

    /// <summary>Haversine-Distanz in Metern.</summary>
    internal static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
