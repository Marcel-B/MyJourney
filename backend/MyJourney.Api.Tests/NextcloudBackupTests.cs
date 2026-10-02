using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyJourney.Api.Backup;

namespace MyJourney.Api.Tests;

public class NextcloudBackupOptionsTests
{
    [Fact]
    public void Load_bevorzugt_Env_gegenueber_Appsettings()
    {
        var options = NextcloudBackupOptions.Load(AppsettingsDisabled(), name => name switch
        {
            "NEXTCLOUD_BACKUP_ENABLED" => "true",
            "NEXTCLOUD_WEBDAV_URL" => "https://cloud.example/remote.php/dav/files/user/MyJourney",
            "NEXTCLOUD_USERNAME" => "user",
            "NEXTCLOUD_APP_PASSWORD" => "app-pass",
            _ => null,
        });

        Assert.True(options.IsConfigured);
        Assert.Equal("https://cloud.example/remote.php/dav/files/user/MyJourney", options.WebDavUrl);
        Assert.Null(options.DisableReason);
    }

    [Fact]
    public void Load_ohne_Env_bleibt_deaktiviert()
    {
        var options = NextcloudBackupOptions.Load(AppsettingsDisabled(), _ => null);

        Assert.False(options.IsConfigured);
        Assert.Equal("NEXTCLOUD_BACKUP_ENABLED ist nicht aktiv", options.DisableReason);
    }

    [Fact]
    public void Load_meldet_fehlende_Zugangsdaten()
    {
        var options = NextcloudBackupOptions.Load(AppsettingsDisabled(), name => name switch
        {
            "NEXTCLOUD_BACKUP_ENABLED" => "true",
            "NEXTCLOUD_WEBDAV_URL" => "https://cloud.example/dav",
            _ => null,
        });

        Assert.False(options.IsConfigured);
        Assert.Equal("NEXTCLOUD_USERNAME fehlt", options.DisableReason);
    }

    [Fact]
    public void Load_uebernimmt_Zeitplan_aus_Env()
    {
        var options = NextcloudBackupOptions.Load(AppsettingsDisabled(), name => name switch
        {
            "NEXTCLOUD_BACKUP_AT" => "04:30",
            "NEXTCLOUD_BACKUP_IDLE_MINUTES" => "5",
            _ => null,
        });

        Assert.Equal(new TimeOnly(4, 30), options.At);
        Assert.Equal(TimeSpan.FromMinutes(5), options.IdleAfter);
    }

    [Fact]
    public void ParseFlag_akzeptiert_Varianten_und_Anfuehrungszeichen()
    {
        Assert.True(NextcloudBackupOptions.ParseFlag("true", false));
        Assert.True(NextcloudBackupOptions.ParseFlag("TRUE", false));
        Assert.True(NextcloudBackupOptions.ParseFlag("1", false));
        Assert.True(NextcloudBackupOptions.ParseFlag("yes", false));
        Assert.True(NextcloudBackupOptions.ParseFlag("\"on\"\r\n", false));
        Assert.False(NextcloudBackupOptions.ParseFlag("false", true));
        Assert.False(NextcloudBackupOptions.ParseFlag("0", true));
        Assert.True(NextcloudBackupOptions.ParseFlag("", true));
        Assert.True(NextcloudBackupOptions.ParseFlag(null, true));
    }

    private static IConfiguration AppsettingsDisabled() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nextcloud:Enabled"] = "false",
                ["Nextcloud:WebDavUrl"] = "",
                ["Nextcloud:Username"] = "",
                ["Nextcloud:AppPassword"] = "",
            })
            .Build();
}

public class WebDavClientTests
{
    [Fact]
    public void CollectionUrls_legt_jede_Ordnerebene_unter_der_Dav_Wurzel_an()
    {
        var urls = HttpWebDavClient.CollectionUrls(
            "https://nx.example.de/remote.php/dav/files/Admin/Backups/MyJourney/");

        Assert.Equal(
        [
            "https://nx.example.de/remote.php/dav/files/Admin/Backups",
            "https://nx.example.de/remote.php/dav/files/Admin/Backups/MyJourney",
        ], urls);
    }

    [Fact]
    public void CollectionUrls_unterstuetzt_den_aelteren_Webdav_Alias()
    {
        var urls = HttpWebDavClient.CollectionUrls(
            "https://nx.example.de/remote.php/webdav/Backups/MyJourney");

        Assert.Equal(
        [
            "https://nx.example.de/remote.php/webdav/Backups",
            "https://nx.example.de/remote.php/webdav/Backups/MyJourney",
        ], urls);
    }

    [Fact]
    public void CollectionUrls_ohne_Unterordner_liefert_die_Basis_URL()
    {
        Assert.Equal(
            ["https://nx.example.de/remote.php/dav/files/Admin"],
            HttpWebDavClient.CollectionUrls("https://nx.example.de/remote.php/dav/files/Admin/"));
    }

    [Fact]
    public void CollectionUrls_bei_unbekanntem_Aufbau_bleibt_ein_einzelnes_MKCOL()
    {
        Assert.Equal(
            ["https://dav.example.de/irgendwo/Backups"],
            HttpWebDavClient.CollectionUrls("https://dav.example.de/irgendwo/Backups/"));
    }
}

public class NextcloudBackupRunnerTests
{
    [Fact]
    public void WeekdayFilename_rotiert_nach_lokalem_Wochentag()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // 2026-10-02 ist ein Freitag.
        Assert.Equal("myjourney-fri.db",
            NextcloudBackupRunner.WeekdayFilename(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), timezone));

        // 23:30 UTC am Freitag ist in Berlin (UTC+2) schon Samstag.
        Assert.Equal("myjourney-sat.db",
            NextcloudBackupRunner.WeekdayFilename(new DateTime(2026, 10, 2, 23, 30, 0, DateTimeKind.Utc), timezone));
    }

    [Fact]
    public async Task ExportSnapshot_erzeugt_konsistente_Kopie()
    {
        var dir = Directory.CreateTempSubdirectory("myjourney-backup-test");
        try
        {
            var dbPath = Path.Combine(dir.FullName, "source.db");
            var snapshotPath = Path.Combine(dir.FullName, "snapshot.db");
            var connectionString = $"Data Source={dbPath}";
            await CreateSampleDb(connectionString);

            await NextcloudBackupRunner.ExportSnapshot(connectionString, snapshotPath, CancellationToken.None);

            await using var connection = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Places";
            Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Run_laedt_Snapshot_mit_Wochentagsnamen_hoch()
    {
        var dir = Directory.CreateTempSubdirectory("myjourney-backup-test");
        try
        {
            var connectionString = $"Data Source={Path.Combine(dir.FullName, "source.db")}";
            await CreateSampleDb(connectionString);

            var webDav = new FakeWebDavClient();
            var runner = new NextcloudBackupRunner(
                webDav, ConfiguredOptions(), connectionString, NullLogger<NextcloudBackupRunner>.Instance);

            var filename = await runner.Run(CancellationToken.None);

            Assert.NotNull(filename);
            Assert.Matches("^myjourney-(mon|tue|wed|thu|fri|sat|sun)\\.db$", filename);
            Assert.Equal("https://cloud.example/dav/MyJourney", webDav.CollectionUrl);
            Assert.Equal(filename, webDav.UploadedFilename);
            Assert.True(webDav.UploadedSize > 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Run_bricht_bei_Upload_Fehler_ohne_Ausnahme_ab()
    {
        var dir = Directory.CreateTempSubdirectory("myjourney-backup-test");
        try
        {
            var connectionString = $"Data Source={Path.Combine(dir.FullName, "source.db")}";
            await CreateSampleDb(connectionString);

            var webDav = new FakeWebDavClient { PutError = "WebDAV PUT fehlgeschlagen (HTTP 507)" };
            var runner = new NextcloudBackupRunner(
                webDav, ConfiguredOptions(), connectionString, NullLogger<NextcloudBackupRunner>.Instance);

            Assert.Null(await runner.Run(CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Run_ohne_Konfiguration_tut_nichts()
    {
        var webDav = new FakeWebDavClient();
        var runner = new NextcloudBackupRunner(
            webDav, new NextcloudBackupOptions(), "Data Source=:memory:", NullLogger<NextcloudBackupRunner>.Instance);

        Assert.Null(await runner.Run(CancellationToken.None));
        Assert.Null(webDav.UploadedFilename);
    }

    private static NextcloudBackupOptions ConfiguredOptions() => new()
    {
        Enabled = true,
        WebDavUrl = "https://cloud.example/dav/MyJourney/",
        Username = "user",
        AppPassword = "app-pass",
    };

    private static async Task CreateSampleDb(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Places (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
            INSERT INTO Places VALUES ('1', 'Lissabon'), ('2', 'Porto');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FakeWebDavClient : IWebDavClient
    {
        public string? CollectionUrl { get; private set; }
        public string? UploadedFilename { get; private set; }
        public long UploadedSize { get; private set; }
        public string? PutError { get; init; }

        public Task<string?> EnsureCollection(
            string baseUrl, string username, string password, TimeSpan timeout, CancellationToken cancellationToken)
        {
            CollectionUrl = baseUrl;
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PutFile(
            string baseUrl, string filename, string localPath, string username, string password,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (PutError is not null)
            {
                return Task.FromResult<string?>(PutError);
            }

            UploadedFilename = filename;
            UploadedSize = new FileInfo(localPath).Length;
            return Task.FromResult<string?>(null);
        }
    }
}
