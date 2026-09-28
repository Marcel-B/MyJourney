namespace MyJourney.Api.Models;

/// <summary>Ein Eintrag kann ein konkreter Ort oder eine ganze Region sein.</summary>
public enum PlaceKind
{
    Place = 0,
    Region = 1,
}

public enum PlaceStatus
{
    Wishlist = 0,
    Visited = 1,
}

public class Place
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public PlaceKind Kind { get; set; } = PlaceKind.Place;

    public PlaceStatus Status { get; set; } = PlaceStatus.Wishlist;

    /// <summary>Region/Gegend, zu der der Ort gehört (Freitext, z. B. "Südnorwegen").</summary>
    public string? Region { get; set; }

    public string? Country { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Notes { get; set; }

    /// <summary>Bewertung 1–5 für bereits besuchte Orte.</summary>
    public int? Rating { get; set; }

    public DateOnly? VisitedAt { get; set; }

    /// <summary>Eignet sich als Zwischenstopp auf einer längeren Reise.</summary>
    public bool IsStopoverCandidate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
