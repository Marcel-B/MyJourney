using MyJourney.Api.Backup;

namespace MyJourney.Api.Monitoring;

/// <summary>
/// Konfiguration für das Monitoring (Prometheus-Metriken und Log-Versand an Loki).
/// Umgebungsvariablen haben Vorrang vor <c>appsettings.json</c> (Abschnitt <c>Monitoring</c>),
/// leere Werte gelten als nicht gesetzt. Ohne Konfiguration bleibt beides einfach aus –
/// wie beim Nextcloud-Backup und beim Login. Konventionen des gemeinsamen Stacks:
/// scripts-Repo, docs/monitoring.md.
/// </summary>
public sealed class MonitoringOptions
{
    public bool MetricsEnabled { get; set; }

    /// <summary>Basis-URL von Loki (z. B. http://monitoring:3100) oder gleich die volle Push-URL.</summary>
    public string? LokiUrl { get; set; }

    /// <summary>Wert des host-Labels in Loki (der Rechner, z. B. "proxmox").</summary>
    public string LokiHost { get; set; } = "";

    public bool LokiConfigured => !string.IsNullOrWhiteSpace(LokiUrl);

    public string? LokiPushUrl
    {
        get
        {
            if (!LokiConfigured) return null;
            var url = LokiUrl!.TrimEnd('/');
            return url.EndsWith("/loki/api/v1/push", StringComparison.Ordinal)
                ? url
                : url + "/loki/api/v1/push";
        }
    }

    public static MonitoringOptions Load(
        IConfiguration configuration,
        Func<string, string?>? getenv = null)
    {
        getenv ??= static name => Environment.GetEnvironmentVariable(name);

        return new MonitoringOptions
        {
            MetricsEnabled = NextcloudBackupOptions.ParseFlag(
                getenv("MYJOURNEY_METRICS_ENABLED"),
                configuration.GetValue("Monitoring:MetricsEnabled", false)),
            LokiUrl = FirstNonEmpty(getenv("MYJOURNEY_LOKI_URL"), configuration["Monitoring:LokiUrl"]),
            LokiHost = FirstNonEmpty(getenv("MYJOURNEY_LOKI_HOST"), configuration["Monitoring:LokiHost"])
                ?? Environment.MachineName.ToLowerInvariant(),
        };
    }

    private static string? FirstNonEmpty(string? env, string? config)
    {
        var fromEnv = NextcloudBackupOptions.Normalize(env);
        if (fromEnv.Length > 0) return fromEnv;

        var fromConfig = NextcloudBackupOptions.Normalize(config);
        return fromConfig.Length > 0 ? fromConfig : null;
    }
}
