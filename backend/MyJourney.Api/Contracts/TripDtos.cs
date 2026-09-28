using MyJourney.Api.Models;

namespace MyJourney.Api.Contracts;

/// <summary>
/// Ein Stopp beim Anlegen/Aktualisieren: entweder placeId eines erfassten Ortes
/// oder ein freier Punkt mit name (Koordinaten optional). Die Reihenfolge ergibt
/// sich aus der Reihenfolge im Array.
/// </summary>
public record TripStopInput(
    Guid? PlaceId = null,
    string? Name = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Notes = null);

public record CreateTripRequest(
    string Name,
    string? Notes = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    List<TripStopInput>? Stops = null);

public record TripStopResponse(
    Guid Id,
    int Order,
    Guid? PlaceId,
    string Name,
    double? Latitude,
    double? Longitude,
    string? Notes)
{
    /// <summary>Name und Koordinaten werden aus dem verknüpften Ort übernommen, wenn vorhanden.</summary>
    public static TripStopResponse From(TripStop stop) => new(
        stop.Id,
        stop.Order,
        stop.PlaceId,
        stop.Place?.Name ?? stop.Name ?? "Unbenannter Stopp",
        stop.Latitude ?? stop.Place?.Latitude,
        stop.Longitude ?? stop.Place?.Longitude,
        stop.Notes);
}

public record TripResponse(
    Guid Id,
    string Name,
    string? Notes,
    DateOnly? StartDate,
    DateOnly? EndDate,
    List<TripStopResponse> Stops,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static TripResponse From(Trip trip) => new(
        trip.Id,
        trip.Name,
        trip.Notes,
        trip.StartDate,
        trip.EndDate,
        trip.Stops.OrderBy(s => s.Order).Select(TripStopResponse.From).ToList(),
        trip.CreatedAt,
        trip.UpdatedAt);
}
