using MyJourney.Api.Services;

namespace MyJourney.Api.Tests;

public class NominatimClientTests
{
    [Fact]
    public void ParseResponse_LiestTrefferMitAdresse()
    {
        // lat/lon kommen bei Nominatim als Strings; "address" liefert Region und Land.
        const string json = """
        [
          {
            "name": "Flåm",
            "display_name": "Flåm, Aurland, Vestland, Norwegen",
            "lat": "60.8622303", "lon": "7.1133718",
            "type": "village",
            "address": { "village": "Flåm", "county": "Vestland", "country": "Norwegen" }
          },
          {
            "name": "Gardasee",
            "display_name": "Gardasee, Italien",
            "lat": "45.6388889", "lon": "10.6833333",
            "type": "water",
            "address": { "state": "Trentino-Südtirol", "country": "Italien" }
          }
        ]
        """;

        var results = NominatimClient.ParseResponse(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("Flåm", results[0].Name);
        Assert.Equal(60.86223, results[0].Latitude, 4);
        Assert.Equal(7.113372, results[0].Longitude, 4);
        Assert.Equal("village", results[0].Category);
        Assert.Equal("Vestland", results[0].Region);
        Assert.Equal("Norwegen", results[0].Country);
        Assert.Equal("Trentino-Südtirol", results[1].Region);
    }

    [Fact]
    public void ParseResponse_FehlenderName_FaelltAufDisplayNameZurueck()
    {
        const string json = """
        [
          { "display_name": "Schwarzwald, Baden-Württemberg, Deutschland", "lat": "48.3", "lon": "8.2", "type": "forest" }
        ]
        """;

        var results = NominatimClient.ParseResponse(json);

        Assert.Single(results);
        Assert.Equal("Schwarzwald", results[0].Name);
        Assert.Null(results[0].Region);
        Assert.Null(results[0].Country);
    }

    [Fact]
    public void ParseResponse_UngueltigeEintraegeWerdenUebersprungen()
    {
        // Ohne Koordinaten oder mit unbrauchbarem lat wird der Eintrag verworfen.
        const string json = """
        [
          { "name": "Ohne Koordinaten", "display_name": "Ohne Koordinaten", "type": "place" },
          { "name": "Kaputt", "display_name": "Kaputt", "lat": "abc", "lon": "7.1", "type": "place" },
          { "name": "Gültig", "display_name": "Gültig, Deutschland", "lat": "51.0", "lon": "10.0", "type": "place" }
        ]
        """;

        var results = NominatimClient.ParseResponse(json);

        Assert.Single(results);
        Assert.Equal("Gültig", results[0].Name);
    }

    [Fact]
    public void ParseResponse_KeinArray_LiefertLeereListe()
    {
        Assert.Empty(NominatimClient.ParseResponse("""{ "error": "oops" }"""));
        Assert.Empty(NominatimClient.ParseResponse("[]"));
    }
}
