namespace MyJourney.Api.Models;

/// <summary>Eine geplante (oder vergangene) Reise mit geordneten Stopps.</summary>
public class Trip
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Notes { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<TripStop> Stops { get; set; } = [];
}

/// <summary>
/// Ein Stopp einer Reise: entweder Verweis auf einen erfassten Ort (PlaceId)
/// oder ein freier Punkt mit eigenem Namen und Koordinaten.
/// </summary>
public class TripStop
{
    public Guid Id { get; set; }

    public Guid TripId { get; set; }

    /// <summary>Reihenfolge innerhalb der Reise, beginnend bei 1.</summary>
    public int Order { get; set; }

    public Guid? PlaceId { get; set; }

    public Place? Place { get; set; }

    public string? Name { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? Notes { get; set; }
}
