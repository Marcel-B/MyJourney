using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyJourney.Api.Contracts;

namespace MyJourney.Api.Services;

/// <summary>
/// Liest Google-Takeout-Exporte von "Maps (Meine Orte)":
/// - "Gespeicherte Orte.json" / "Saved Places.json": GeoJSON mit den Sternorten
///   (altes Format mit "Location"/"Title" und aktuelles Format mit "location")
/// - "Bewertungen.json" / "Reviews.json": GeoJSON mit den eigenen Google-Bewertungen
/// - Listen-CSVs (z. B. "Favoriten.csv"): Spalten Title/Titel, Note/Notiz, URL
/// </summary>
public static partial class GoogleTakeoutParser
{
    [GeneratedRegex(@"!3d(-?\d+(?:\.\d+)?)!4d(-?\d+(?:\.\d+)?)")]
    private static partial Regex DataCoordinatesRegex();

    [GeneratedRegex(@"@(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)")]
    private static partial Regex AtCoordinatesRegex();

    public static List<ImportedPlaceCandidate> Parse(string content)
    {
        var trimmed = content.TrimStart('﻿', ' ', '\r', '\n', '\t');
        return trimmed.StartsWith('{') ? ParseGeoJson(trimmed) : ParseCsv(trimmed);
    }

    private static List<ImportedPlaceCandidate> ParseGeoJson(string json)
    {
        var result = new List<ImportedPlaceCandidate>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Die JSON-Datei enthält kein GeoJSON mit \"features\".");
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var props)) continue;

            // "Bewertungen.json": Features mit eigener Google-Bewertung (1–5 Sterne).
            if (props.TryGetProperty("five_star_rating_published", out _))
            {
                var review = ParseReviewFeature(feature, props);
                if (review is not null) result.Add(review);
                continue;
            }

            var name = GetString(props, "Title") ?? GetString(props, "title");
            string? address = null, countryCode = null;
            // Älteres Takeout-Format: "Location" mit "Business Name"/"Name"/"Address".
            if (props.TryGetProperty("Location", out var location) && location.ValueKind == JsonValueKind.Object)
            {
                name ??= GetString(location, "Business Name") ?? GetString(location, "Name");
                address = GetString(location, "Address");
            }
            // Aktuelles Takeout-Format: "location" mit "name"/"address"/"country_code".
            if (props.TryGetProperty("location", out var newLocation) && newLocation.ValueKind == JsonValueKind.Object)
            {
                name ??= GetString(newLocation, "name");
                address ??= GetString(newLocation, "address");
                countryCode = GetString(newLocation, "country_code");
            }
            // Einträge ohne Ortsnamen (z. B. "keine Informationen verfügbar") überspringen.
            if (string.IsNullOrWhiteSpace(name)) continue;

            var (lat, lon) = GetCoordinates(feature);
            result.Add(new ImportedPlaceCandidate(
                name.Trim(), lat, lon, address,
                VisitedAt: ParseDate(props),
                Country: string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant()));
        }

        return result;
    }

    /// <summary>
    /// Ein Feature aus "Bewertungen.json": location {name, address, country_code},
    /// five_star_rating_published, date, optional review_text_published.
    /// </summary>
    private static ImportedPlaceCandidate? ParseReviewFeature(JsonElement feature, JsonElement props)
    {
        string? name = null, address = null, countryCode = null;
        if (props.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object)
        {
            name = GetString(location, "name");
            address = GetString(location, "address");
            countryCode = GetString(location, "country_code");
        }
        // Ohne Ortsnamen (Google liefert manche Bewertungen ohne "location") können wir nichts anlegen.
        if (string.IsNullOrWhiteSpace(name)) return null;

        int? rating = null;
        if (props.TryGetProperty("five_star_rating_published", out var ratingElement) &&
            ratingElement.ValueKind == JsonValueKind.Number &&
            ratingElement.TryGetInt32(out var ratingValue) &&
            ratingValue is >= 1 and <= 5)
        {
            rating = ratingValue;
        }

        var visitedAt = ParseDate(props);

        var reviewText = GetString(props, "review_text_published");
        var notes = string.IsNullOrWhiteSpace(reviewText) ? address : reviewText.Trim();

        var (lat, lon) = GetCoordinates(feature);
        return new ImportedPlaceCandidate(
            name.Trim(), lat, lon, notes,
            Rating: rating,
            VisitedAt: visitedAt,
            Country: string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant());
    }

    /// <summary>Liest das "date"-Feld eines Takeout-Features als Datum.</summary>
    private static DateOnly? ParseDate(JsonElement props)
    {
        if (GetString(props, "date") is { } date &&
            DateTimeOffset.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return DateOnly.FromDateTime(parsed.UtcDateTime);
        }

        return null;
    }

    private static (double? Lat, double? Lon) GetCoordinates(JsonElement feature)
    {
        if (feature.TryGetProperty("geometry", out var geometry) &&
            geometry.ValueKind == JsonValueKind.Object &&
            geometry.TryGetProperty("coordinates", out var coords) &&
            coords.ValueKind == JsonValueKind.Array &&
            coords.GetArrayLength() >= 2 &&
            coords[0].ValueKind == JsonValueKind.Number &&
            coords[1].ValueKind == JsonValueKind.Number)
        {
            // GeoJSON: [Längengrad, Breitengrad]
            return (coords[1].GetDouble(), coords[0].GetDouble());
        }

        return (null, null);
    }

    private static List<ImportedPlaceCandidate> ParseCsv(string content)
    {
        var rows = ReadCsv(content);
        if (rows.Count == 0) return [];

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var titleIdx = header.FindIndex(h => h is "title" or "titel" or "name");
        var noteIdx = header.FindIndex(h => h is "note" or "notiz" or "comment" or "kommentar");
        var urlIdx = header.FindIndex(h => h == "url");

        if (titleIdx < 0)
        {
            throw new FormatException("Die CSV-Datei hat keine Spalte \"Title\"/\"Titel\" – ist es eine Google-Takeout-Liste?");
        }

        var result = new List<ImportedPlaceCandidate>();
        foreach (var row in rows.Skip(1))
        {
            if (titleIdx >= row.Count) continue;
            var name = row[titleIdx].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var note = noteIdx >= 0 && noteIdx < row.Count ? row[noteIdx].Trim() : null;

            double? lat = null, lon = null;
            if (urlIdx >= 0 && urlIdx < row.Count)
            {
                var match = DataCoordinatesRegex().Match(row[urlIdx]);
                if (!match.Success) match = AtCoordinatesRegex().Match(row[urlIdx]);
                if (match.Success)
                {
                    lat = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    lon = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            result.Add(new ImportedPlaceCandidate(name, lat, lon, string.IsNullOrWhiteSpace(note) ? null : note));
        }

        return result;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Minimaler CSV-Reader mit Unterstützung für Anführungszeichen und Zeilenumbrüche in Feldern.</summary>
    private static List<List<string>> ReadCsv(string content)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    current.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    current.Add(field.ToString());
                    field.Clear();
                    if (current.Count > 1 || current[0].Length > 0) rows.Add(current);
                    current = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            if (current.Count > 1 || current[0].Length > 0) rows.Add(current);
        }

        return rows;
    }
}
