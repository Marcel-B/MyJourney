using MyJourney.Api.Security;

namespace MyJourney.Api.Tests;

public class InviteRulesTests
{
    [Fact]
    public void NewToken_IstUrlTauglichUndZufaellig()
    {
        var a = InviteRules.NewToken();
        var b = InviteRules.NewToken();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
    }

    [Theory]
    [InlineData("partnerin", "partnerin")]
    [InlineData("  Anna  ", "Anna")]
    [InlineData("anna.meier", "anna.meier")]
    public void ValidateUsername_AkzeptiertGueltigeNamen(string input, string expected)
    {
        var (username, error) = InviteRules.ValidateUsername(input);

        Assert.Null(error);
        Assert.Equal(expected, username);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("anna meier")]
    public void ValidateUsername_LehntUngueltigeNamenAb(string? input)
    {
        var (username, error) = InviteRules.ValidateUsername(input);

        Assert.Null(username);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateUsername_LehntZuLangeNamenAb()
    {
        var (username, error) = InviteRules.ValidateUsername(new string('a', 51));

        Assert.Null(username);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("geheim123")]
    [InlineData("12345678")]
    public void ValidatePassword_AkzeptiertGueltigePasswoerter(string password)
    {
        Assert.Null(InviteRules.ValidatePassword(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kurz")]
    public void ValidatePassword_LehntUngueltigePasswoerterAb(string? password)
    {
        Assert.NotNull(InviteRules.ValidatePassword(password));
    }
}
