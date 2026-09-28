using Microsoft.Extensions.Configuration;
using MyJourney.Api.Security;

namespace MyJourney.Api.Tests;

public class OAuthTokenServiceTests
{
    private static OAuthTokenService Create(params string[] apiKeys)
    {
        var values = apiKeys
            .Select((key, index) => new KeyValuePair<string, string?>($"Security:ApiKeys:{index}", key));
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new OAuthTokenService(config);
    }

    [Fact]
    public void Ausgestelltes_Token_Ist_Gueltig_Und_Traegt_Claims()
    {
        var service = Create("key-eins");

        var token = service.Issue("code", TimeSpan.FromMinutes(5), new Dictionary<string, string>
        {
            ["chal"] = "challenge-wert",
            ["ruri"] = "https://chatgpt.com/callback",
        });

        var claims = service.Validate(token, "code");
        Assert.NotNull(claims);
        Assert.Equal("challenge-wert", claims["chal"]);
        Assert.Equal("https://chatgpt.com/callback", claims["ruri"]);
    }

    [Fact]
    public void Falscher_Typ_Wird_Abgelehnt()
    {
        var service = Create("key-eins");
        var token = service.Issue("code", TimeSpan.FromMinutes(5));
        Assert.Null(service.Validate(token, "access"));
    }

    [Fact]
    public void Abgelaufenes_Token_Wird_Abgelehnt()
    {
        var service = Create("key-eins");
        var token = service.Issue("access", TimeSpan.FromSeconds(-1));
        Assert.Null(service.Validate(token, "access"));
    }

    [Fact]
    public void Manipuliertes_Token_Wird_Abgelehnt()
    {
        var service = Create("key-eins");
        var token = service.Issue("access", TimeSpan.FromMinutes(5));
        var manipulated = token[..^4] + "aaaa";
        Assert.Null(service.Validate(manipulated, "access"));
        Assert.Null(service.Validate("mj.kein-token", "access"));
        Assert.Null(service.Validate("völlig-anders", "access"));
    }

    [Fact]
    public void Anderer_ApiKey_Bedeutet_Ungueltige_Tokens()
    {
        var tokenVonAlt = Create("alter-key").Issue("access", TimeSpan.FromMinutes(5));
        Assert.Null(Create("neuer-key").Validate(tokenVonAlt, "access"));
    }

    [Fact]
    public void Zweiter_Konfigurierter_Key_Validiert_Auch()
    {
        // Tokens werden mit dem ersten Key signiert, aber gegen alle geprüft –
        // so bleiben sie beim Hinzufügen eines zweiten Keys gültig.
        var token = Create("key-eins", "key-zwei").Issue("access", TimeSpan.FromMinutes(5));
        Assert.NotNull(Create("key-eins").Validate(token, "access"));
    }
}
