using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MyJourney.Api.Services;

/// <summary>
/// Verteilt "Daten haben sich geändert"-Signale an alle verbundenen Clients (SSE unter /api/events).
/// Die Version zählt monoton hoch und startet pro Prozess bei den aktuellen Ticks – so sieht ein
/// Client nach einem Reconnect (oder Server-Neustart) an der abweichenden Version, dass er
/// zwischenzeitlich etwas verpasst haben könnte, ohne dass der Server Historie vorhalten muss.
/// </summary>
public sealed class ChangeNotifier(IHttpContextAccessor httpContextAccessor)
{
    public sealed record DataChangedEvent(long Version, string? Origin);

    private readonly ConcurrentDictionary<Guid, Channel<DataChangedEvent>> _subscribers = new();
    private long _version = DateTime.UtcNow.Ticks;

    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Meldet eine Datenänderung an alle Abonnenten. Als Origin geht die X-Client-Id des
    /// auslösenden Requests mit, damit der Verursacher sein eigenes Echo ignorieren kann.
    /// </summary>
    public void NotifyDataChanged()
    {
        var version = Interlocked.Increment(ref _version);
        var origin = httpContextAccessor.HttpContext?.Request.Headers["X-Client-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 64) origin = null;

        var evt = new DataChangedEvent(version, origin);
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Abonniert Änderungssignale. Der Kanal puffert nur das jeweils neueste Ereignis –
    /// mehr braucht ein Client nicht, er lädt ohnehin den kompletten Bestand neu.
    /// </summary>
    public (Guid Id, ChannelReader<DataChangedEvent> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<DataChangedEvent>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id) => _subscribers.TryRemove(id, out _);
}
