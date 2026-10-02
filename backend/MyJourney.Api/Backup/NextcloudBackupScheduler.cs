using Microsoft.Data.Sqlite;
using MyJourney.Api.Services;

namespace MyJourney.Api.Backup;

/// <summary>
/// Plant die Nextcloud-Backups: einmal täglich zur konfigurierten Uhrzeit und zusätzlich
/// einige Minuten nach der letzten Datenänderung (Signal vom <see cref="ChangeNotifier"/>),
/// damit frische Änderungen nicht bis zur nächsten Nacht ungesichert bleiben.
/// Eine Stempeldatei neben der Datenbank merkt sich den letzten Tageslauf über Neustarts hinweg.
/// </summary>
public sealed class NextcloudBackupScheduler(
    NextcloudBackupRunner runner,
    ChangeNotifier notifier,
    NextcloudBackupOptions options,
    string connectionString,
    ILogger<NextcloudBackupScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _runLock = new(1, 1);
    private DateOnly? _lastRunDate;
    private long? _lastFailureMs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!runner.IsEnabled)
        {
            logger.LogInformation("Nextcloud-Backup deaktiviert ({Reason})", options.DisableReason);
            return;
        }

        _lastRunDate = ReadStamp();
        logger.LogInformation(
            "Nextcloud-Backup aktiv (täglich {At} {Tz}, nach Änderungen {Idle} min)",
            options.BackupAt, options.Timezone, options.IdleMinutes);

        var idleLoop = RunIdleLoop(stoppingToken);

        try
        {
            await MaybeRunDaily(stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Tick);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await MaybeRunDaily(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await idleLoop.ConfigureAwait(false);
    }

    /// <summary>
    /// Wartet auf Änderungssignale und sichert, sobald nach der letzten Änderung
    /// <c>IdleMinutes</c> lang Ruhe war. Jedes neue Signal startet die Wartezeit neu.
    /// </summary>
    private async Task RunIdleLoop(CancellationToken stoppingToken)
    {
        if (options.IdleAfter <= TimeSpan.Zero)
        {
            return;
        }

        var (id, reader) = notifier.Subscribe();
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out _))
                {
                }

                // Solange weitere Änderungen eintrudeln, weiter warten (der Kanal
                // puffert nur das neueste Ereignis, mehr ist nicht nötig).
                var quiet = false;
                while (!quiet)
                {
                    await Task.Delay(options.IdleAfter, stoppingToken).ConfigureAwait(false);
                    quiet = true;
                    while (reader.TryRead(out _))
                    {
                        quiet = false;
                    }
                }

                await RunBackup("idle", stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            notifier.Unsubscribe(id);
        }
    }

    private async Task MaybeRunDaily(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, options.TimeZoneInfo);
        var localToday = DateOnly.FromDateTime(local);
        if (_lastRunDate == localToday)
        {
            return;
        }

        if (local < local.Date.Add(options.At.ToTimeSpan()))
        {
            return;
        }

        if (_lastFailureMs is long failure &&
            Environment.TickCount64 - failure < (long)RetryAfter.TotalMilliseconds)
        {
            return;
        }

        var ok = await RunBackup("daily", stoppingToken).ConfigureAwait(false);
        if (ok)
        {
            WriteStamp(localToday);
            _lastRunDate = localToday;
            _lastFailureMs = null;
        }
        else
        {
            _lastFailureMs = Environment.TickCount64;
        }
    }

    private async Task<bool> RunBackup(string kind, CancellationToken stoppingToken)
    {
        await _runLock.WaitAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            logger.LogInformation("Nextcloud-Backup startet ({Kind})", kind);
            var filename = await runner.Run(stoppingToken).ConfigureAwait(false);
            return filename is not null;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private string? StampPath
    {
        get
        {
            var dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return null;
            }

            return Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".",
                "last_nextcloud_backup_date");
        }
    }

    private DateOnly? ReadStamp()
    {
        try
        {
            var path = StampPath;
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            return DateOnly.TryParse(File.ReadAllText(path).Trim(), out var date) ? date : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteStamp(DateOnly date)
    {
        try
        {
            var path = StampPath;
            if (path is null)
            {
                return;
            }

            File.WriteAllText(path, date.ToString("yyyy-MM-dd") + "\n");
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Backup-Stempeldatei konnte nicht geschrieben werden");
        }
    }
}
