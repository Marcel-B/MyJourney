using MyJourney.Api.Models;

namespace MyJourney.Api.Contracts;

public record PlaceResponse(
    Guid Id,
    string Name,
    PlaceKind Kind,
    PlaceStatus Status,
    string? Region,
    string? Country,
    double? Latitude,
    double? Longitude,
    string? Notes,
    int? Rating,
    DateOnly? VisitedAt,
    bool IsStopoverCandidate,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static PlaceResponse From(Place p) => new(
        p.Id, p.Name, p.Kind, p.Status, p.Region, p.Country,
        p.Latitude, p.Longitude, p.Notes, p.Rating, p.VisitedAt,
        p.IsStopoverCandidate, p.CreatedAt, p.UpdatedAt);
}

public record CreatePlaceRequest(
    string Name,
    PlaceKind Kind = PlaceKind.Place,
    PlaceStatus Status = PlaceStatus.Wishlist,
    string? Region = null,
    string? Country = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Notes = null,
    int? Rating = null,
    DateOnly? VisitedAt = null,
    bool IsStopoverCandidate = false);

public record UpdatePlaceRequest(
    string Name,
    PlaceKind Kind,
    PlaceStatus Status,
    string? Region,
    string? Country,
    double? Latitude,
    double? Longitude,
    string? Notes,
    int? Rating,
    DateOnly? VisitedAt,
    bool IsStopoverCandidate);

/// <summary>Kurzform, um einen Wunschort als besucht zu markieren.</summary>
public record MarkVisitedRequest(DateOnly? VisitedAt = null, int? Rating = null, string? Notes = null);
