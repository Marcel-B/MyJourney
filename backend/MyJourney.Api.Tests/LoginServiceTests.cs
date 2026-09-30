using Microsoft.Extensions.Configuration;
using MyJourney.Api.Security;

namespace MyJourney.Api.Tests;

public class LoginServiceTests
{
    private static LoginService Create(string? username, string? passwordHash)
    {
        var values = new Dictionary<string, string?>
        {
            ["Security:Login:Username"] = username,
            ["Security:Login:PasswordHash"] = passwordHash,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new LoginService(config);
    }

    [Fact]
    public void HashPassword_ErzeugtPruefbarenHash()
    {
        var hash = LoginService.HashPassword("geheim123");

        Assert.StartsWith("pbkdf2.", hash);
        Assert.True(LoginService.VerifyPassword("geheim123", hash));
        Assert.False(LoginService.VerifyPassword("falsch", hash));
    }

    [Fact]
    public void HashPassword_SalzMachtHashesUnterschiedlich()
    {
        Assert.NotEqual(LoginService.HashPassword("geheim123"), LoginService.HashPassword("geheim123"));
    }

    [Fact]
    public void Verify_RichtigeZugangsdaten()
    {
        var service = Create("marcel", LoginService.HashPassword("geheim123"));

        Assert.True(service.IsConfigured);
        Assert.True(service.Verify("marcel", "geheim123"));
    }

    [Theory]
    [InlineData("marcel", "falsch")]
    [InlineData("jemand", "geheim123")]
    [InlineData(null, "geheim123")]
    [InlineData("marcel", null)]
    public void Verify_FalscheZugangsdaten(string? username, string? password)
    {
        var service = Create("marcel", LoginService.HashPassword("geheim123"));

        Assert.False(service.Verify(username, password));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("marcel", null)]
    [InlineData(null, "pbkdf2.1.AA==.AA==")]
    [InlineData("", "  ")]
    public void OhneVollstaendigeKonfiguration_IstLoginAus(string? username, string? passwordHash)
    {
        var service = Create(username, passwordHash);

        Assert.False(service.IsConfigured);
        Assert.False(service.Verify("marcel", "geheim123"));
    }

    [Theory]
    [InlineData("kein-hash")]
    [InlineData("pbkdf2.abc.AA==.AA==")]
    [InlineData("pbkdf2.1.kein-base64.AA==")]
    [InlineData("md5.1.AA==.AA==")]
    public void VerifyPassword_LehntKaputteHashesAb(string storedHash)
    {
        Assert.False(LoginService.VerifyPassword("geheim123", storedHash));
    }
}
