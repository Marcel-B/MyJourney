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

    [Theory]
    [InlineData(53.55, 9.99, 54.0, 9.99, 0)]    // genau nach Norden
    [InlineData(53.55, 9.99, 53.0, 9.99, 180)]  // genau nach Süden
    [InlineData(53.55, 9.99, 53.55, 10.5, 90)]  // nach Osten
    [InlineData(53.55, 9.99, 53.55, 9.5, 270)]  // nach Westen
    public void BearingDeg_LiefertHimmelsrichtung(double lat1, double lon1, double lat2, double lon2, double expected)
    {
        var bearing = NearbyPlaces.BearingDeg(lat1, lon1, lat2, lon2);
        Assert.True(Math.Abs(NearbyPlaces.AngleDeltaDeg(bearing, expected)) < 1,
            $"Peilung {bearing}° weicht zu stark von {expected}° ab.");
    }

    [Theory]
    [InlineData(10, 350, 20)]
    [InlineData(350, 10, -20)]
    [InlineData(90, 270, 180)]
    [InlineData(45, 45, 0)]
    public void AngleDeltaDeg_NimmtDenKuerzerenWeg(double a, double b, double expected)
    {
        Assert.Equal(expected, NearbyPlaces.AngleDeltaDeg(a, b), 6);
    }

    [Fact]
    public void FindAhead_LiefertNurOrteImKorridorVoraus()
    {
        // Fahrt nach Norden ab Hamburg: Kiel liegt voraus, Lüneburg (Süden) und
        // ein Ort im Westen liegen außerhalb des ±45°-Korridors.
        var places = new List<Place>
        {
            MakePlace("Kiel", 54.3233, 10.1228),
            MakePlace("Lüneburg", 53.2464, 10.4115),
            MakePlace("Westlich", 53.55, 9.0),
        };

        var hits = NearbyPlaces.FindAhead(places, 53.5503, 9.9920, radiusKm: 200, headingDeg: 0);

        Assert.Single(hits);
        Assert.Equal("Kiel", hits[0].Place.Name);
        Assert.InRange(hits[0].BearingDeg, 0, 45);
    }

    [Fact]
    public void FindAhead_ErkenntGegenrichtungUeberGespeicherteFahrtrichtung()
    {
        // Zwei Rastplätze voraus: einer wurde in Fahrtrichtung Norden erfasst (richtige
        // Seite), einer in Fahrtrichtung Süden (Gegenrichtung), einer ohne Richtung.
        var richtigeSeite = MakePlace("Rasthof Nord", 53.8, 9.99);
        richtigeSeite.HeadingDeg = 10;
        var gegenrichtung = MakePlace("Rasthof Süd", 53.9, 9.99);
        gegenrichtung.HeadingDeg = 185;
        var unbekannt = MakePlace("Rasthof Alt", 54.0, 9.99);

        var hits = NearbyPlaces.FindAhead([richtigeSeite, gegenrichtung, unbekannt], 53.5503, 9.9920, 100, headingDeg: 0);

        Assert.Equal(3, hits.Count);
        Assert.True(hits.Single(h => h.Place.Name == "Rasthof Nord").SameDirection);
        Assert.False(hits.Single(h => h.Place.Name == "Rasthof Süd").SameDirection);
        Assert.Null(hits.Single(h => h.Place.Name == "Rasthof Alt").SameDirection);
    }

    [Fact]
    public void FindAhead_KorridorUmschliesstNordrichtungUeberNull()
    {
        // Fahrtrichtung 350°: ein Ort bei Peilung 5° liegt im ±45°-Korridor,
        // obwohl die Gradzahlen über die 0°-Grenze springen.
        var places = new List<Place> { MakePlace("Leicht rechts", 54.0, 10.06) };

        var hits = NearbyPlaces.FindAhead(places, 53.5503, 9.9920, 100, headingDeg: 350);

        Assert.Single(hits);
    }

    [Theory]
    [InlineData(360, 0)]
    [InlineData(-10, 350)]
    [InlineData(725, 5)]
    public void NormalizeHeading_BringtWerteInDenBereich(double input, double expected)
    {
        Assert.Equal(expected, NearbyPlaces.NormalizeHeading(input), 6);
    }
}
