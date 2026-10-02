using Microsoft.Data.Sqlite;

namespace MyJourney.Api.Backup;

/// <summary>
/// Führt einen Backup-Lauf aus: konsistenter SQLite-Snapshot per <c>VACUUM INTO</c>
/// (blockiert laufende Schreiber nicht), dann Upload per WebDAV in die Nextcloud.
/// Die Dateinamen rotieren über den Wochentag (myjourney-mon.db … myjourney-sun.db),
/// es liegen also maximal sieben Stände in der Nextcloud.
/// </summary>
public sealed class NextcloudBackupRunner(
    IWebDavClient webDav,
    NextcloudBackupOptions options,
    string connectionString,
    ILogger<NextcloudBackupRunner> logger)
{
    public bool IsEnabled => options.IsConfigured;

    /// <summary>Gibt bei Erfolg den hochgeladenen Dateinamen zurück, sonst <c>null</c>.</summary>
    public async Task<string?> Run(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var filename = WeekdayFilename(DateTime.UtcNow, options.TimeZoneInfo);
        var tmp = Path.Combine(Path.GetTempPath(), $"myjourney_backup_{Guid.NewGuid():N}.db");

        try
        {
            var baseUrl = options.WebDavUrl!.TrimEnd('/');
            var collectionError = await webDav
                .EnsureCollection(baseUrl, options.Username!, options.AppPassword!, options.HttpTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (collectionError is not null)
            {
                logger.LogError("Nextcloud-Backup fehlgeschlagen: {Reason}", collectionError);
                return null;
            }

            await ExportSnapshot(connectionString, tmp, cancellationToken).ConfigureAwait(false);
            var size = new FileInfo(tmp).Length;
            logger.LogInformation("Nextcloud-Backup Upload startet: {Filename} ({Size} Bytes)", filename, size);

            var uploadError = await webDav
                .PutFile(baseUrl, filename, tmp, options.Username!, options.AppPassword!, options.HttpTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (uploadError is not null)
            {
                logger.LogError("Nextcloud-Backup fehlgeschlagen: {Reason}", uploadError);
                return null;
            }

            logger.LogInformation("Nextcloud-Backup hochgeladen: {Filename}", filename);
            return filename;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Nextcloud-Backup fehlgeschlagen");
            return null;
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    /// <summary>Erzeugt per <c>VACUUM INTO</c> eine konsistente, kompaktierte Kopie der Datenbank.</summary>
    public static async Task ExportSnapshot(string connectionString, string targetPath, CancellationToken cancellationToken)
    {
        // VACUUM INTO verweigert existierende Zieldateien.
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path";
        command.Parameters.AddWithValue("$path", targetPath);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string WeekdayFilename(DateTime utcNow, TimeZoneInfo timezone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), timezone);
        var name = local.DayOfWeek switch
        {
            DayOfWeek.Monday => "mon",
            DayOfWeek.Tuesday => "tue",
            DayOfWeek.Wednesday => "wed",
            DayOfWeek.Thursday => "thu",
            DayOfWeek.Friday => "fri",
            DayOfWeek.Saturday => "sat",
            _ => "sun",
        };
        return $"myjourney-{name}.db";
    }
}
