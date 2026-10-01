using MyJourney.Api.Models;

namespace MyJourney.Api.Contracts;

/// <summary>
/// Eine von einem KI-Assistenten (z. B. ChatGPT) erzeugte Reise-Datei
/// nach dem Schema "myjourney-trip", Version 1.
/// </summary>
public record TripImportFile(
    string? Schema = null,
    int? SchemaVersion = null,
    TripImportTrip? Trip = null);

public record TripImportTrip(
    string? Name = null,
    string? Notes = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    List<TripImportStop>? Stops = null);

/// <summary>
/// Ein Stopp aus der Import-Datei. Verweist per placeId auf einen erfassten Ort,
/// wird über den Namen einem vorhandenen Ort zugeordnet oder legt einen neuen
/// Wunschlisten-Ort an (es sei denn, saveAsPlace ist false: dann bleibt er ein
/// freier Routenpunkt ohne Eintrag in der Ortsliste).
/// </summary>
public record TripImportStop(
    string? Name = null,
    Guid? PlaceId = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Notes = null,
    string? Country = null,
    string? Region = null,
    OvernightType? Overnight = null,
    bool? IsStopoverCandidate = null,
    bool? SaveAsPlace = null);

public record TripImportResult(
    TripResponse Trip,
    List<string> CreatedPlaces,
    List<string> LinkedPlaces,
    List<string> FreeStops);
