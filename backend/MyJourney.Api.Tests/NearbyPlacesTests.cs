using MyJourney.Api.Models;
using MyJourney.Api.Services;

namespace MyJourney.Api.Tests;

public class NearbyPlacesTests
{
    private static Place MakePlace(string name, double? lat, double? lon) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Latitude = lat,
        Longitude = lon,
    };

    [Fact]
    public void Find_FiltertAufRadius_SortiertAufsteigend()
    {
        // Bezugspunkt: Hamburg Rathaus. Lüneburg ~40 km, Stade ~32 km, Blankenese ~11 km.
        var places = new List<Place>
        {
            MakePlace("Lüneburg", 53.2464, 10.4115),
            MakePlace("Blankenese", 53.5586, 9.7904),
            MakePlace("Stade", 53.5993, 9.4764),
            MakePlace("Ohne Koordinaten", null, null),
        };

        var hits = NearbyPlaces.Find(places, 53.5503, 9.9920, radiusKm: 35);

        Assert.Equal(2, hits.Count);
        Assert.Equal("Blankenese", hits[0].Place.Name);
        Assert.Equal("Stade", hits[1].Place.Name);
        Assert.True(hits[0].DistanceKm < hits[1].DistanceKm);
        Assert.InRange(hits[0].DistanceKm, 10, 16);
        Assert.InRange(hits[1].DistanceKm, 30, 35);
    }

    [Fact]
    public void Find_Standardradius20km_LaesstWeitereOrteWeg()
    {
        var places = new List<Place>
        {
            MakePlace("Nah", 53.56, 9.99),
            MakePlace("Fern", 53.2464, 10.4115), // ~40 km
        };

        var hits = NearbyPlaces.Find(places, 53.5503, 9.9920, NearbyPlaces.DefaultRadiusKm);

        Assert.Single(hits);
        Assert.Equal("Nah", hits[0].Place.Name);
    }

    [Fact]
    public void Find_GleicheDistanz_SortiertNachName()
    {
        var places = new List<Place>
        {
            MakePlace("Beta", 53.5503, 9.9920),
            MakePlace("Alpha", 53.5503, 9.9920),
        };

        var hits = NearbyPlaces.Find(places, 53.5503, 9.9920, 5);

        Assert.Equal(["Alpha", "Beta"], hits.Select(h => h.Place.Name));
        Assert.All(hits, h => Assert.Equal(0, h.DistanceKm));
    }
}
