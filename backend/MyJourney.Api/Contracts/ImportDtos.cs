namespace MyJourney.Api.Contracts;

public record ImportResult(int Total, int Imported, int Skipped, List<string> ImportedNames, List<string> SkippedNames);

public record ImportedPlaceCandidate(string Name, double? Latitude, double? Longitude, string? Notes);
