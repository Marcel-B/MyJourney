using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyJourney.Api.Monitoring;

namespace MyJourney.Api.Tests;

public class MonitoringOptionsTests
{
    private static IConfiguration EmptyConfig => new ConfigurationBuilder().Build();

    private static Func<string, string?> Env(Dictionary<string, string?> values)
        => name => values.TryGetValue(name, out var value) ? value : null;

    [Fact]
    public void Ohne_Konfiguration_ist_alles_aus()
    {
        var options = MonitoringOptions.Load(EmptyConfig, Env([]));

        Assert.False(options.MetricsEnabled);
        Assert.False(options.LokiConfigured);
        Assert.Null(options.LokiPushUrl);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("\"yes\"")]
    public void Metrics_Flag_versteht_uebliche_Schreibweisen(string value)
    {
        var options = MonitoringOptions.Load(EmptyConfig, Env(new()
        {
            ["MYJOURNEY_METRICS_ENABLED"] = value,
        }));

        Assert.True(options.MetricsEnabled);
    }

    [Fact]
    public void Loki_Basis_Url_bekommt_den_Push_Pfad()
    {
        var options = MonitoringOptions.Load(EmptyConfig, Env(new()
        {
            ["MYJOURNEY_LOKI_URL"] = "http://monitoring:3100/",
        }));

        Assert.True(options.LokiConfigured);
        Assert.Equal("http://monitoring:3100/loki/api/v1/push", options.LokiPushUrl);
    }

    [Fact]
    public void Volle_Push_Url_bleibt_unveraendert()
    {
        var options = MonitoringOptions.Load(EmptyConfig, Env(new()
        {
            ["MYJOURNEY_LOKI_URL"] = "http://monitoring:3100/loki/api/v1/push",
        }));

        Assert.Equal("http://monitoring:3100/loki/api/v1/push", options.LokiPushUrl);
    }

    [Fact]
    public void Umgebungsvariable_schlaegt_appsettings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Monitoring:LokiUrl"] = "http://aus-config:3100",
            ["Monitoring:LokiHost"] = "config-host",
        }).Build();

        var options = MonitoringOptions.Load(config, Env(new()
        {
            ["MYJOURNEY_LOKI_URL"] = "http://aus-env:3100",
        }));

        Assert.Equal("http://aus-env:3100/loki/api/v1/push", options.LokiPushUrl);
        Assert.Equal("config-host", options.LokiHost);
    }

    [Fact]
    public void Host_faellt_auf_den_Maschinennamen_zurueck()
    {
        var options = MonitoringOptions.Load(EmptyConfig, Env([]));

        Assert.Equal(Environment.MachineName.ToLowerInvariant(), options.LokiHost);
    }
}

public class LokiShipperTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(Uri? Url, string Body)> Requests { get; } = [];
        public bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("Loki nicht erreichbar");
            Requests.Add((request.RequestUri, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private static MonitoringOptions Options => new()
    {
        LokiUrl = "http://monitoring:3100",
        LokiHost = "proxmox",
    };

    [Fact]
    public async Task Flush_schickt_einen_Stream_pro_Level()
    {
        var handler = new CapturingHandler();
        using var shipper = new LokiShipper(Options, handler);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        shipper.Enqueue(at, "info", "erste Zeile");
        shipper.Enqueue(at.AddSeconds(1), "info", "zweite Zeile");
        shipper.Enqueue(at.AddSeconds(2), "error", "kaputt");

        Assert.True(await shipper.FlushAsync(CancellationToken.None));

        var (url, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://monitoring:3100/loki/api/v1/push", url!.ToString());

        using var json = JsonDocument.Parse(body);
        var streams = json.RootElement.GetProperty("streams");
        Assert.Equal(2, streams.GetArrayLength());

        var info = streams.EnumerateArray()
            .Single(s => s.GetProperty("stream").GetProperty("level").GetString() == "info");
        Assert.Equal("myjourney", info.GetProperty("stream").GetProperty("app").GetString());
        Assert.Equal("proxmox", info.GetProperty("stream").GetProperty("host").GetString());

        var values = info.GetProperty("values");
        Assert.Equal(2, values.GetArrayLength());
        Assert.Equal((1_700_000_000_000 * 1_000_000L).ToString(), values[0][0].GetString());
        Assert.Equal("erste Zeile", values[0][1].GetString());
        Assert.Equal("zweite Zeile", values[1][1].GetString());
    }

    [Fact]
    public async Task Leere_Warteschlange_schickt_nichts()
    {
        var handler = new CapturingHandler();
        using var shipper = new LokiShipper(Options, handler);

        Assert.True(await shipper.FlushAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Fehler_beim_Push_wirft_nicht()
    {
        var handler = new CapturingHandler { Fail = true };
        using var shipper = new LokiShipper(Options, handler);
        shipper.Enqueue(DateTimeOffset.UtcNow, "info", "Zeile");

        Assert.False(await shipper.FlushAsync(CancellationToken.None));
    }

    [Fact]
    public void Ohne_Loki_Url_gibt_es_keinen_Shipper()
    {
        Assert.Throws<ArgumentException>(() => new LokiShipper(new MonitoringOptions()));
    }
}

public class LokiLoggerProviderTests
{
    [Theory]
    [InlineData(LogLevel.Trace, "debug")]
    [InlineData(LogLevel.Debug, "debug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warning")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Critical, "error")]
    public void Level_werden_auf_Loki_Namen_abgebildet(LogLevel level, string expected)
    {
        Assert.Equal(expected, LokiLoggerProvider.MapLevel(level));
    }

    [Fact]
    public void Zeile_traegt_Kategorie_und_Exception()
    {
        var plain = LokiLoggerProvider.FormatLine("MyJourney.Api.Test", "Hallo", null);
        Assert.Equal("[MyJourney.Api.Test] Hallo", plain);

        var withException = LokiLoggerProvider.FormatLine(
            "MyJourney.Api.Test", "Hallo", new InvalidOperationException("kaputt"));
        Assert.StartsWith("[MyJourney.Api.Test] Hallo", withException);
        Assert.Contains("kaputt", withException);
    }

    [Fact]
    public void Logger_reiht_Zeilen_in_die_Warteschlange_ein()
    {
        using var shipper = new LokiShipper(new MonitoringOptions { LokiUrl = "http://monitoring:3100" });
        var provider = new LokiLoggerProvider(shipper);
        var logger = provider.CreateLogger("MyJourney.Api.Test");

        logger.LogWarning("etwas ist seltsam");

        Assert.True(shipper.Reader.TryRead(out var entry));
        Assert.Equal("warning", entry!.Level);
        Assert.Equal("[MyJourney.Api.Test] etwas ist seltsam", entry.Line);
    }
}
