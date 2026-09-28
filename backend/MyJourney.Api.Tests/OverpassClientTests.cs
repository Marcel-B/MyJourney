using MyJourney.Api.Services;

namespace MyJourney.Api.Tests;

public class OverpassClientTests
{
    [Fact]
    public void ParseResponse_LiestNodesUndWays_SortiertNachDistanz()
    {
        // Node mit lat/lon direkt, Way mit "center", ein Element ohne Namen (wird übersprungen).
        const string json = """
        {
          "elements": [
            {
              "type": "way", "id": 2,
              "center": { "lat": 48.1400, "lon": 11.5800 },
              "tags": { "name": "Englischer Garten", "leisure": "park" }
            },
            {
              "type": "node", "id": 1, "lat": 48.1375, "lon": 11.5756,
              "tags": { "name": "Neues Rathaus", "tourism": "attraction" }
            },
            {
              "type": "node", "id": 3, "lat": 48.1376, "lon": 11.5757,
              "tags": { "amenity": "bench" }
            }
          ]
        }
        """;

        var pois = OverpassClient.ParseResponse(json, 48.1374, 11.5755);

        Assert.Equal(2, pois.Count);
        Assert.Equal("Neues Rathaus", pois[0].Name);
        Assert.Equal("attraction", pois[0].Category);
        Assert.True(pois[0].DistanceMeters < pois[1].DistanceMeters);
        Assert.Equal("Englischer Garten", pois[1].Name);
        Assert.Equal("park", pois[1].Category);
    }

    [Fact]
    public void ParseResponse_EntferntDoppelteNamen()
    {
        const string json = """
        {
          "elements": [
            { "type": "node", "id": 1, "lat": 48.0, "lon": 11.0, "tags": { "name": "Campingplatz Seeblick", "tourism": "camp_site" } },
            { "type": "node", "id": 2, "lat": 48.0001, "lon": 11.0001, "tags": { "name": "campingplatz seeblick", "tourism": "camp_site" } }
          ]
        }
        """;

        var pois = OverpassClient.ParseResponse(json, 48.0, 11.0);
        Assert.Single(pois);
    }

    [Fact]
    public void ParseResponse_OhneElements_LeereListe()
    {
        Assert.Empty(OverpassClient.ParseResponse("""{ "remark": "timeout" }""", 48, 11));
    }

    [Fact]
    public void DistanceMeters_LiefertPlausibleWerte()
    {
        // München Marienplatz -> Frauenkirche: ca. 340 m Luftlinie.
        var distance = OverpassClient.DistanceMeters(48.1374, 11.5755, 48.1386, 11.5717);
        Assert.InRange(distance, 250, 450);
    }
}
