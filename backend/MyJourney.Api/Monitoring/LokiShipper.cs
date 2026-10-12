using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MyJourney.Api.Monitoring;

/// <summary>
/// Schickt Logzeilen gesammelt an Loki (<c>/loki/api/v1/push</c>). Ist Loki nicht erreichbar
/// oder die Warteschlange voll, werden Zeilen verworfen statt zu blockieren – das Container-Log
/// (docker compose logs) bleibt davon unberührt. Verwendet bewusst einen eigenen HttpClient
/// ohne Logging-Handler, sonst erzeugt jeder Push neue Logzeilen für den nächsten Push.
/// </summary>
public sealed class LokiShipper : IDisposable
{
    internal sealed record Entry(DateTimeOffset At, string Level, string Line);

    private const int QueueCapacity = 2048;
    internal const int MaxBatchSize = 500;

    private readonly Channel<Entry> _queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    private readonly HttpClient _http;
    private readonly string _pushUrl;
    private readonly string _host;

    public LokiShipper(MonitoringOptions options, HttpMessageHandler? handler = null)
    {
        _pushUrl = options.LokiPushUrl
            ?? throw new ArgumentException("Loki ist nicht konfiguriert (LokiUrl fehlt).", nameof(options));
        _host = options.LokiHost;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    internal ChannelReader<Entry> Reader => _queue.Reader;

    public void Enqueue(DateTimeOffset at, string level, string line)
        => _queue.Writer.TryWrite(new Entry(at, level, line));

    /// <summary>
    /// Schickt bis zu <see cref="MaxBatchSize"/> wartende Zeilen als einen Push an Loki.
    /// Fehler werden geschluckt (false), die Zeilen sind dann weg.
    /// </summary>
    internal async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        while (entries.Count < MaxBatchSize && _queue.Reader.TryRead(out var entry))
        {
            entries.Add(entry);
        }
        if (entries.Count == 0) return true;

        var payload = BuildPayload(entries, "myjourney", _host);
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_pushUrl, content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Baut den Push-Body: ein Stream pro Level, Labels nur app/host/level
    /// (scripts-Repo, docs/monitoring.md), Zeitstempel in Nanosekunden und je Stream aufsteigend.
    /// </summary>
    internal static string BuildPayload(IReadOnlyList<Entry> entries, string app, string host)
    {
        var streams = entries
            .GroupBy(e => e.Level)
            .Select(group => new
            {
                stream = new Dictionary<string, string>
                {
                    ["app"] = app,
                    ["host"] = host,
                    ["level"] = group.Key,
                },
                values = group
                    .OrderBy(e => e.At)
                    .Select(e => new[]
                    {
                        (e.At.ToUnixTimeMilliseconds() * 1_000_000L).ToString(),
                        e.Line,
                    })
                    .ToArray(),
            })
            .ToArray();

        return JsonSerializer.Serialize(new { streams });
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Hintergrunddienst des <see cref="LokiShipper"/>: sammelt kurz, schickt dann den Stapel.
/// Beim Herunterfahren wird der Rest mit knappem Zeitfenster noch losgeschickt.
/// </summary>
public sealed class LokiShipperService(LokiShipper shipper) : BackgroundService
{
    private static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await shipper.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(BatchDelay, stoppingToken);
                await shipper.FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Herunterfahren – unten den Rest losschicken.
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (shipper.Reader.TryPeek(out _) && await shipper.FlushAsync(cts.Token))
        {
        }
    }
}
