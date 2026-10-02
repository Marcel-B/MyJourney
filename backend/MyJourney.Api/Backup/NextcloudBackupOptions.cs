namespace MyJourney.Api.Backup;

/// <summary>
/// Konfiguration für das Nextcloud-Backup (WebDAV). Umgebungsvariablen haben Vorrang
/// vor <c>appsettings.json</c> (Abschnitt <c>Nextcloud</c>), leere Werte gelten als nicht gesetzt.
/// Ohne vollständige Konfiguration bleibt das Backup einfach aus.
/// </summary>
public sealed class NextcloudBackupOptions
{
    public bool Enabled { get; set; }
    public string? WebDavUrl { get; set; }
    public string? Username { get; set; }
    public string? AppPassword { get; set; }
    public string BackupAt { get; set; } = "03:00";
    public string Timezone { get; set; } = "Europe/Berlin";
    public int IdleMinutes { get; set; } = 10;
    public int HttpTimeoutSeconds { get; set; } = 300;

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(WebDavUrl) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(AppPassword);

    public string? DisableReason
    {
        get
        {
            if (IsConfigured) return null;
            if (!Enabled) return "NEXTCLOUD_BACKUP_ENABLED ist nicht aktiv";
            if (string.IsNullOrWhiteSpace(WebDavUrl)) return "NEXTCLOUD_WEBDAV_URL fehlt";
            if (string.IsNullOrWhiteSpace(Username)) return "NEXTCLOUD_USERNAME fehlt";
            if (string.IsNullOrWhiteSpace(AppPassword)) return "NEXTCLOUD_APP_PASSWORD fehlt";
            return "Konfiguration unvollständig";
        }
    }

    public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(Timezone);

    public TimeOnly At => TimeOnly.TryParse(BackupAt, out var at) ? at : new TimeOnly(3, 0);

    public TimeSpan IdleAfter => TimeSpan.FromMinutes(Math.Max(IdleMinutes, 0));

    public TimeSpan HttpTimeout => TimeSpan.FromSeconds(Math.Max(HttpTimeoutSeconds, 1));

    public static NextcloudBackupOptions Load(
        IConfiguration configuration,
        Func<string, string?>? getenv = null)
    {
        getenv ??= static name => Environment.GetEnvironmentVariable(name);

        return new NextcloudBackupOptions
        {
            Enabled = ParseFlag(
                getenv("NEXTCLOUD_BACKUP_ENABLED"),
                configuration.GetValue("Nextcloud:Enabled", false)),
            WebDavUrl = FirstNonEmpty(getenv("NEXTCLOUD_WEBDAV_URL"), configuration["Nextcloud:WebDavUrl"]),
            Username = FirstNonEmpty(getenv("NEXTCLOUD_USERNAME"), configuration["Nextcloud:Username"]),
            AppPassword = FirstNonEmpty(getenv("NEXTCLOUD_APP_PASSWORD"), configuration["Nextcloud:AppPassword"]),
            BackupAt = FirstNonEmpty(getenv("NEXTCLOUD_BACKUP_AT"), configuration["Nextcloud:BackupAt"]) ?? "03:00",
            Timezone = FirstNonEmpty(getenv("NEXTCLOUD_BACKUP_TIMEZONE"), configuration["Nextcloud:Timezone"])
                ?? "Europe/Berlin",
            IdleMinutes = ParseInt(
                getenv("NEXTCLOUD_BACKUP_IDLE_MINUTES"),
                configuration.GetValue("Nextcloud:IdleMinutes", 10)),
            HttpTimeoutSeconds = ParseInt(
                getenv("NEXTCLOUD_BACKUP_HTTP_TIMEOUT_SECONDS"),
                configuration.GetValue("Nextcloud:HttpTimeoutSeconds", 300)),
        };
    }

    /// <summary>Trimmt Whitespace und entfernt umschließende Anführungszeichen (häufig in .env-Dateien).</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static bool ParseFlag(string? value, bool fallback)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return fallback;

        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(string? env, string? config)
    {
        var fromEnv = Normalize(env);
        if (fromEnv.Length > 0) return fromEnv;

        var fromConfig = Normalize(config);
        return fromConfig.Length > 0 ? fromConfig : null;
    }

    private static int ParseInt(string? env, int fallback)
    {
        var normalized = Normalize(env);
        return int.TryParse(normalized, out var parsed) ? parsed : fallback;
    }
}
