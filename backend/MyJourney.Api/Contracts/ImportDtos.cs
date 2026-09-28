namespace MyJourney.Api.Contracts;

public record ImportResult(
    int Total,
    int Imported,
    int Updated,
    int Skipped,
    List<string> ImportedNames,
    List<string> UpdatedNames,
    List<string> SkippedNames);

public record ImportedPlaceCandidate(
    string Name,
    double? Latitude,
    double? Longitude,
    string? Notes,
    int? Rating = null,
    DateOnly? VisitedAt = null,
    string? Country = null);
