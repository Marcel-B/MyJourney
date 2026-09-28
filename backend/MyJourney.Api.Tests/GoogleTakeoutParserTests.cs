using MyJourney.Api.Services;

namespace MyJourney.Api.Tests;

public class GoogleTakeoutParserTests
{
    [Fact]
    public void Parse_GespeicherteOrte_GeoJson()
    {
        const string json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "geometry": { "coordinates": [10.398, 63.4305], "type": "Point" },
              "properties": {
                "Location": {
                  "Address": "Munkegata 1, Trondheim, Norwegen",
                  "Business Name": "Nidarosdom"
                },
                "Title": "Nidarosdom"
              },
              "type": "Feature"
            }
          ]
        }
        """;

        var result = GoogleTakeoutParser.Parse(json);

        var place = Assert.Single(result);
        Assert.Equal("Nidarosdom", place.Name);
        Assert.Equal(63.4305, place.Latitude);
        Assert.Equal(10.398, place.Longitude);
        Assert.Equal("Munkegata 1, Trondheim, Norwegen", place.Notes);
        Assert.Null(place.Rating);
        Assert.Null(place.VisitedAt);
    }

    [Fact]
    public void Parse_GespeicherteOrte_NeuesFormat_MitKleingeschriebenerLocation()
    {
        const string json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "geometry": { "coordinates": [10.398, 63.4305], "type": "Point" },
              "properties": {
                "date": "2024-03-10T08:00:00Z",
                "google_maps_url": "http://maps.google.com/?cid=123",
                "location": {
                  "address": "Munkegata 1, Trondheim, Norwegen",
                  "country_code": "NO",
                  "name": "Nidarosdom"
                }
              },
              "type": "Feature"
            },
            {
              "geometry": { "coordinates": [7.0, 50.0], "type": "Point" },
              "properties": {
                "date": "2024-03-11T08:00:00Z",
                "google_maps_url": "http://maps.google.com/?q=x&ftid=0:0",
                "Comment": "Für diesen gespeicherten Ort sind keine Informationen verfügbar."
              },
              "type": "Feature"
            }
          ]
        }
        """;

        var result = GoogleTakeoutParser.Parse(json);

        var place = Assert.Single(result);
        Assert.Equal("Nidarosdom", place.Name);
        Assert.Equal(63.4305, place.Latitude);
        Assert.Equal("NO", place.Country);
        Assert.Null(place.Rating);
    }

    [Fact]
    public void Parse_Bewertungen_GeoJson()
    {
        const string json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "geometry": { "coordinates": [11.576, 48.1372], "type": "Point" },
              "properties": {
                "date": "2023-08-15T12:30:00.000000Z",
                "five_star_rating_published": 4,
                "google_maps_url": "https://www.google.com/maps/place//data=!4m2!3m1!1s0x0:0x0",
                "location": {
                  "address": "Marienplatz 8, 80331 München, Deutschland",
                  "country_code": "de",
                  "name": "Neues Rathaus"
                },
                "review_text_published": "Beeindruckendes Gebäude, das Glockenspiel lohnt sich."
              },
              "type": "Feature"
            }
          ]
        }
        """;

        var result = GoogleTakeoutParser.Parse(json);

        var place = Assert.Single(result);
        Assert.Equal("Neues Rathaus", place.Name);
        Assert.Equal(48.1372, place.Latitude);
        Assert.Equal(11.576, place.Longitude);
        Assert.Equal(4, place.Rating);
        Assert.Equal(new DateOnly(2023, 8, 15), place.VisitedAt);
        Assert.Equal("DE", place.Country);
        Assert.Equal("Beeindruckendes Gebäude, das Glockenspiel lohnt sich.", place.Notes);
    }

    [Fact]
    public void Parse_Bewertung_OhneReviewText_NutztAdresseAlsNotiz()
    {
        const string json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "geometry": { "coordinates": [13.377, 52.5163], "type": "Point" },
              "properties": {
                "date": "2019-05-01T09:00:00.000000Z",
                "five_star_rating_published": 5,
                "google_maps_url": "https://www.google.com/maps/place//data=!4m2!3m1!1s0x0:0x1",
                "location": {
                  "address": "Pariser Platz, 10117 Berlin, Deutschland",
                  "country_code": "DE",
                  "name": "Brandenburger Tor"
                }
              },
              "type": "Feature"
            }
          ]
        }
        """;

        var result = GoogleTakeoutParser.Parse(json);

        var place = Assert.Single(result);
        Assert.Equal(5, place.Rating);
        Assert.Equal("Pariser Platz, 10117 Berlin, Deutschland", place.Notes);
    }

    [Fact]
    public void Parse_Bewertung_OhneLocation_WirdUebersprungen()
    {
        const string json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "geometry": { "coordinates": [7.1, 50.7], "type": "Point" },
              "properties": {
                "date": "2020-01-02T10:00:00.000000Z",
                "five_star_rating_published": 3,
                "google_maps_url": "https://www.google.com/maps/place//data=!4m2!3m1!1s0x0:0x2"
              },
              "type": "Feature"
            },
            {
              "geometry": { "coordinates": [9.9937, 53.5511], "type": "Point" },
              "properties": {
                "date": "2022-07-20T18:00:00.000000Z",
                "five_star_rating_published": 5,
                "google_maps_url": "https://www.google.com/maps/place//data=!4m2!3m1!1s0x0:0x3",
                "location": {
                  "address": "Platz der Deutschen Einheit 1, 20457 Hamburg, Deutschland",
                  "country_code": "DE",
                  "name": "Elbphilharmonie"
                },
                "review_text_published": "Tolle Akustik."
              },
              "type": "Feature"
            }
          ]
        }
        """;

        var result = GoogleTakeoutParser.Parse(json);

        var place = Assert.Single(result);
        Assert.Equal("Elbphilharmonie", place.Name);
    }

    [Fact]
    public void Parse_GemischteDatei_SterneOrteUndBewertungen()
    {
        const string json = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "geometry": { "coordinates": [10.398, 63.4305], "type": "Point" },
              "properties": { "Title": "Nidarosdom" },
              "type": "Feature"
            },
            {
              "geometry": { "coordinates": [11.576, 48.1372], "type": "Point" },
              "properties": {
                "date": "2023-08-15T12:30:00.000000Z",
                "five_star_rating_published": 4,
                "google_maps_url": "https://www.google.com/maps/place//data=!4m2!3m1!1s0x0:0x0",
                "location": { "name": "Neues Rathaus" }
              },
              "type": "Feature"
            }
          ]
        }
        """;

        var result = GoogleTakeoutParser.Parse(json);

        Assert.Equal(2, result.Count);
        Assert.Null(result[0].Rating);
        Assert.Equal(4, result[1].Rating);
    }

    [Fact]
    public void Parse_ListenCsv()
    {
        const string csv = """
        Title,Note,URL,Comment
        Nidarosdom,Unbedingt ansehen,"https://www.google.com/maps/place/data=!3d63.4305!4d10.398",
        """;

        var result = GoogleTakeoutParser.Parse(csv);

        var place = Assert.Single(result);
        Assert.Equal("Nidarosdom", place.Name);
        Assert.Equal(63.4305, place.Latitude);
        Assert.Equal(10.398, place.Longitude);
        Assert.Equal("Unbedingt ansehen", place.Notes);
    }

    [Fact]
    public void Parse_UngueltigesJson_WirftFormatException()
    {
        Assert.Throws<FormatException>(() => GoogleTakeoutParser.Parse("{ \"kein\": \"geojson\" }"));
    }
}
