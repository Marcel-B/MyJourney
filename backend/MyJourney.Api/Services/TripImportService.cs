using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Models;

namespace MyJourney.Api.Services;

/// <summary>
/// Importiert eine Reise aus einer "myjourney-trip"-Datei (z. B. von ChatGPT erzeugt).
/// Stopps werden erfassten Orten zugeordnet (per placeId oder Name) oder als neue
/// Wunschlisten-Orte angelegt; mit saveAsPlace=false bleiben sie freie Routenpunkte.
/// </summary>
public static class TripImportService
{
    public const string SchemaName = "myjourney-trip";
    public const int SchemaVersion = 1;

    /// <summary>JSON Schema (Draft 2020-12) für die Import-Datei – wird auch an ChatGPT mitgegeben.</summary>
    public const string SchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://github.com/Marcel-B/MyJourney/schema/myjourney-trip-v1.json",
          "title": "MyJourney-Reise (Import, Version 1)",
          "description": "Eine geplante Reise mit Stopps in Fahrreihenfolge für den Import in MyJourney.",
          "type": "object",
          "required": ["schema", "schemaVersion", "trip"],
          "properties": {
            "schema": { "const": "myjourney-trip" },
            "schemaVersion": { "const": 1 },
            "trip": {
              "type": "object",
              "required": ["name", "stops"],
              "properties": {
                "name": { "type": "string", "minLength": 1, "description": "Name der Reise, z. B. \"Südnorwegen 2027\"." },
                "notes": { "type": ["string", "null"], "description": "Notizen zur Reise (Idee, Jahreszeit, Besonderheiten)." },
                "startDate": { "type": ["string", "null"], "format": "date", "description": "Startdatum als YYYY-MM-DD, falls bekannt." },
                "endDate": { "type": ["string", "null"], "format": "date", "description": "Enddatum als YYYY-MM-DD, falls bekannt." },
                "stops": {
                  "type": "array",
                  "minItems": 1,
                  "description": "Die Stopps der Reise in Fahrreihenfolge.",
                  "items": { "$ref": "#/$defs/stop" }
                }
              }
            }
          },
          "$defs": {
            "stop": {
              "type": "object",
              "required": ["name"],
              "properties": {
                "name": { "type": "string", "minLength": 1, "description": "Name des Ortes/Stopps, z. B. \"Kristiansand\"." },
                "placeId": { "type": ["string", "null"], "description": "Id eines bereits in MyJourney erfassten Ortes (aus dem Bestands-Export, places[].id). Wenn gesetzt, wird der Stopp mit diesem Ort verknüpft." },
                "latitude": { "type": ["number", "null"], "minimum": -90, "maximum": 90, "description": "Breitengrad (WGS84) – bitte angeben, damit der Stopp auf der Karte erscheint." },
                "longitude": { "type": ["number", "null"], "minimum": -180, "maximum": 180, "description": "Längengrad (WGS84)." },
                "notes": { "type": ["string", "null"], "description": "Notizen zum Stopp: Was ansehen? Warum hier halten oder übernachten?" },
                "country": { "type": ["string", "null"], "description": "Land auf Deutsch, z. B. \"Norwegen\"." },
                "region": { "type": ["string", "null"], "description": "Region/Gegend, z. B. \"Südnorwegen\"." },
                "overnight": { "enum": ["None", "Stellplatz", "Campingplatz", "Frei", "Parkplatz", null], "description": "Übernachtungsmöglichkeit: Stellplatz (Wohnmobil-Stellplatz), Campingplatz, Frei (frei stehen), Parkplatz (z. B. Autobahn-/Rastplatz) oder None." },
                "isStopoverCandidate": { "type": ["boolean", "null"], "description": "true, wenn sich der Ort auch als Zwischenstopp auf anderen Reisen eignet." },
                "saveAsPlace": { "type": ["boolean", "null"], "description": "false für reine Routenpunkte (z. B. eine Fähre), die nicht als Ort in MyJourney gespeichert werden sollen. Standard: true." }
              }
            }
          }
        }
        """;

    /// <summary>Prüft die Datei formal. Ein leeres Ergebnis bedeutet: gültig.</summary>
    public static Dictionary<string, string[]> Validate(TripImportFile? file)
    {
        var errors = new Dictionary<string, string[]>();
        if (file is null)
        {
            errors["file"] = ["Die Datei enthält kein JSON-Objekt."];
            return errors;
        }

        if (file.Schema is not null && !string.Equals(file.Schema, SchemaName, StringComparison.OrdinalIgnoreCase))
        {
            errors["schema"] = [$"Unbekanntes Schema \"{file.Schema}\" – erwartet wird \"{SchemaName}\"."];
        }
        if (file.SchemaVersion is not null && file.SchemaVersion != SchemaVersion)
        {
            errors["schemaVersion"] = [$"Schema-Version {file.SchemaVersion} wird nicht unterstützt – erwartet wird {SchemaVersion}."];
        }

        if (file.Trip is null)
        {
            errors["trip"] = ["Es fehlt das Objekt \"trip\" mit der Reise."];
            return errors;
        }
        if (string.IsNullOrWhiteSpace(file.Trip.Name))
        {
            errors["trip.name"] = ["Die Reise braucht einen Namen."];
        }
        if (file.Trip.Stops is null || file.Trip.Stops.Count == 0)
        {
            errors["trip.stops"] = ["Die Reise braucht mindestens einen Stopp."];
        }
        else
        {
            var unnamed = file.Trip.Stops
                .Select((stop, index) => (stop, index))
                .Where(x => string.IsNullOrWhiteSpace(x.stop.Name) && x.stop.PlaceId is null)
                .Select(x => x.index + 1)
                .ToList();
            if (unnamed.Count > 0)
            {
                errors["trip.stops"] = [$"Stopp {string.Join(", ", unnamed)}: jeder Stopp braucht einen Namen (oder eine placeId)."];
            }
        }

        return errors;
    }

    /// <summary>
    /// Wendet eine (bereits validierte) Import-Datei an: legt fehlende Orte als Wunschziele an
    /// und erstellt die Reise. Unbekannte placeIds führen zu Validierungsfehlern statt eines Imports.
    /// </summary>
    public static async Task<(TripImportResult? Result, Dictionary<string, string[]>? Errors)> ImportAsync(
        JourneyDbContext db, TripImportFile file)
    {
        var trip = file.Trip!;
        var stops = trip.Stops!;

        var places = await db.Places.ToListAsync();
        var byId = places.ToDictionary(p => p.Id);
        var byName = new Dictionary<string, Place>();
        foreach (var place in places)
        {
            byName.TryAdd(place.Name.ToLowerInvariant(), place);
        }

        var unknownIds = stops
            .Where(s => s.PlaceId is not null && !byId.ContainsKey(s.PlaceId.Value))
            .Select(s => s.PlaceId!.Value.ToString())
            .Distinct()
            .ToList();
        if (unknownIds.Count > 0)
        {
            return (null, new Dictionary<string, string[]>
            {
                ["trip.stops"] = [$"Unbekannte placeId(s): {string.Join(", ", unknownIds)}. Bitte die Ids aus dem aktuellen Bestands-Export verwenden oder die placeId weglassen."],
            });
        }

        var now = DateTime.UtcNow;
        var createdPlaces = new List<string>();
        var linkedPlaces = new List<string>();
        var freeStops = new List<string>();
        var stopInputs = new List<TripStopInput>();

        foreach (var stop in stops)
        {
            var name = stop.Name?.Trim();

            Place? resolved = null;
            if (stop.PlaceId is not null)
            {
                resolved = byId[stop.PlaceId.Value];
            }
            else if (name is not null && byName.TryGetValue(name.ToLowerInvariant(), out var match))
            {
                resolved = match;
            }

            if (resolved is not null)
            {
                linkedPlaces.Add(resolved.Name);
                stopInputs.Add(new TripStopInput(PlaceId: resolved.Id, Notes: stop.Notes));
                continue;
            }

            if (stop.SaveAsPlace is false)
            {
                freeStops.Add(name!);
                stopInputs.Add(new TripStopInput(
                    Name: name, Latitude: stop.Latitude, Longitude: stop.Longitude, Notes: stop.Notes));
                continue;
            }

            var place = new Place
            {
                Id = Guid.NewGuid(),
                Name = name!,
                Kind = PlaceKind.Place,
                Status = PlaceStatus.Wishlist,
                Region = Trimmed(stop.Region),
                Country = Trimmed(stop.Country),
                Latitude = stop.Latitude,
                Longitude = stop.Longitude,
                Overnight = stop.Overnight ?? OvernightType.None,
                IsStopoverCandidate = stop.IsStopoverCandidate ?? false,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Places.Add(place);
            byName[place.Name.ToLowerInvariant()] = place;
            byId[place.Id] = place;
            createdPlaces.Add(place.Name);
            stopInputs.Add(new TripStopInput(PlaceId: place.Id, Notes: stop.Notes));
        }

        // Neue Orte zuerst speichern, damit BuildTrip ihre Ids in der Datenbank findet.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        var request = new CreateTripRequest(
            trip.Name!.Trim(), trip.Notes, trip.StartDate, trip.EndDate, stopInputs);
        var (entity, error) = await Endpoints.TripEndpoints.BuildTrip(db, request, existing: null);
        if (error is not null)
        {
            await transaction.RollbackAsync();
            return (null, new Dictionary<string, string[]> { ["trip"] = ["Die Reise konnte nicht angelegt werden."] });
        }

        db.Trips.Add(entity!);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var created = await db.Trips.AsNoTracking()
            .Include(t => t.Stops)
            .ThenInclude(s => s.Place)
            .FirstAsync(t => t.Id == entity!.Id);

        return (new TripImportResult(TripResponse.From(created), createdPlaces, linkedPlaces, freeStops), null);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
