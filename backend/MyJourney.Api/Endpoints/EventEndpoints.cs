using System.Text.Json;
using MyJourney.Api.Services;

namespace MyJourney.Api.Endpoints;

/// <summary>
/// Server-Sent Events unter /api/events: hält eine offene Verbindung und schickt bei jeder
/// Datenänderung ein kleines JSON-Ereignis ("version" + "origin"). Die Clients laden daraufhin
/// ihren Bestand neu – der Stream transportiert bewusst keine Nutzdaten.
/// </summary>
public static class EventEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (HttpContext context, ChangeNotifier notifier, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            // Reverse-Proxies anweisen, die Antwort nicht zu puffern.
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var (id, reader) = notifier.Subscribe();
            try
            {
                // Direkt den aktuellen Stand schicken: daran erkennt ein Client nach einem
                // Reconnect, ob sich zwischenzeitlich etwas geändert hat.
                await WriteEventAsync(context.Response, new ChangeNotifier.DataChangedEvent(notifier.Version, null), cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(25));
                    try
                    {
                        var evt = await reader.ReadAsync(timeout.Token);
                        await WriteEventAsync(context.Response, evt, cancellationToken);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Nichts passiert: Keepalive-Kommentar, damit Proxies und der
                        // Tailscale Funnel die Verbindung nicht als tot einstufen.
                        await context.Response.WriteAsync(": keepalive\n\n", cancellationToken);
                        await context.Response.Body.FlushAsync(cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Client hat die Verbindung beendet – normaler Abschluss.
            }
            finally
            {
                notifier.Unsubscribe(id);
            }
        })
        .ExcludeFromDescription(); // interner Stream, gehört nicht ins OpenAPI-Schema

        return app;
    }

    private static async Task WriteEventAsync(
        HttpResponse response, ChangeNotifier.DataChangedEvent evt, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(evt, JsonOptions)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
