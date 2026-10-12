using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Data;
using MyJourney.Api.Models;
using Prometheus;

namespace MyJourney.Api.Monitoring;

/// <summary>
/// Eigene Messwerte nach den Konventionen des gemeinsamen Monitoring-Stacks
/// (scripts-Repo, docs/monitoring.md): Präfix <c>myjourney_</c>, Zähler enden auf
/// <c>_total</c>, Labels nur mit wenigen festen Werten, keine Inhalte oder Namen.
/// Die Zähler laufen immer mit; exportiert wird nur, wenn /metrics aktiv ist.
/// </summary>
public static class AppMetrics
{
    public static readonly Counter DataChanges = Metrics.CreateCounter(
        "myjourney_data_changes_total",
        "Gespeicherte Datenänderungen an Orten, Reisen und Stopps.");

    public static readonly Gauge LiveClients = Metrics.CreateGauge(
        "myjourney_live_clients",
        "Offene Live-Update-Verbindungen (/api/events).");

    public static readonly Counter BackupRuns = Metrics.CreateCounter(
        "myjourney_backup_runs_total",
        "Nextcloud-Backup-Läufe nach Ergebnis.",
        "result");

    private static readonly Gauge Places = Metrics.CreateGauge(
        "myjourney_places",
        "Orte in der Datenbank nach Status.",
        "status");

    private static readonly Gauge Trips = Metrics.CreateGauge(
        "myjourney_trips",
        "Reisen in der Datenbank.");

    /// <summary>Füllt die Bestands-Gauges bei jedem Scrape frisch aus der Datenbank.</summary>
    public static void RegisterDbGauges(IServiceProvider services)
    {
        Metrics.DefaultRegistry.AddBeforeCollectCallback(async cancellationToken =>
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JourneyDbContext>();
            Places.WithLabels("wishlist")
                .Set(await db.Places.CountAsync(p => p.Status == PlaceStatus.Wishlist, cancellationToken));
            Places.WithLabels("visited")
                .Set(await db.Places.CountAsync(p => p.Status == PlaceStatus.Visited, cancellationToken));
            Trips.Set(await db.Trips.CountAsync(cancellationToken));
        });
    }
}
