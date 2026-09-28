using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyJourney.Api.Contracts;

namespace MyJourney.Api.Services;

/// <summary>
/// Liest Google-Takeout-Exporte von "Maps (Meine Orte)":
/// - "Gespeicherte Orte.json" / "Saved Places.json": GeoJSON mit den Sternorten
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

            var name = GetString(props, "Title") ?? GetString(props, "title");
            string? address = null;
            if (props.TryGetProperty("Location", out var location) && location.ValueKind == JsonValueKind.Object)
            {
                name ??= GetString(location, "Business Name") ?? GetString(location, "Name");
                address = GetString(location, "Address");
            }
            if (string.IsNullOrWhiteSpace(name)) continue;

            double? lat = null, lon = null;
            if (feature.TryGetProperty("geometry", out var geometry) &&
                geometry.ValueKind == JsonValueKind.Object &&
                geometry.TryGetProperty("coordinates", out var coords) &&
                coords.ValueKind == JsonValueKind.Array &&
                coords.GetArrayLength() >= 2)
            {
                // GeoJSON: [Längengrad, Breitengrad]
                lon = coords[0].GetDouble();
                lat = coords[1].GetDouble();
            }

            result.Add(new ImportedPlaceCandidate(name.Trim(), lat, lon, address));
        }

        return result;
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
