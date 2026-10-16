using System.Globalization;
using System.Text.Json;
using MyJourney.Api.Contracts;

namespace MyJourney.Api.Services;

/// <summary>
/// Ortssuche (Geocoding) über OpenStreetMap Nominatim: Suchtext -> Orte mit Name,
/// Koordinaten, Region und Land. Läuft über das Backend, damit User-Agent und
/// Rate-Limit der Nominatim-Nutzungsregeln zentral eingehalten werden.
/// </summary>
public class NominatimClient(HttpClient http)
{
    private const string Endpoint = "https://nominatim.openstreetmap.org/search";

    // Nominatim erlaubt höchstens 1 Anfrage pro Sekunde pro Anwendung.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    public async Task<List<GeocodeResult>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var url = $"{Endpoint}?format=jsonv2&addressdetails=1&accept-language=de&limit={limit}&q={Uri.EscapeDataString(query)}";

        await Gate.WaitAsync(ct);
        try
        {
            var wait = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(json);
        }
        finally
        {
            _lastRequestUtc = DateTime.UtcNow;
            Gate.Release();
        }
    }

    /// <summary>Nominatim-Antwort (jsonv2) in Suchtreffer übersetzen.</summary>
    internal static List<GeocodeResult> ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var result = new List<GeocodeResult>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            // lat/lon kommen bei Nominatim als Strings.
            var lat = GetCoordinate(element, "lat");
            var lon = GetCoordinate(element, "lon");
            if (lat is null || lon is null) continue;

            var displayName = GetString(element, "display_name") ?? "";
            var name = GetString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = displayName.Split(',')[0].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            string? region = null, country = null;
            if (element.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
            {
                region = GetString(address, "state") ?? GetString(address, "county") ?? GetString(address, "region");
                country = GetString(address, "country");
            }

            result.Add(new GeocodeResult(
                name,
                displayName,
                Math.Round(lat.Value, 6),
                Math.Round(lon.Value, 6),
                GetString(element, "type") ?? "place",
                region,
                country));
        }

        return result;
    }

    private static double? GetCoordinate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
