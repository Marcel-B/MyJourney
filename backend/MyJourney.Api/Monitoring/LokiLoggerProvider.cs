namespace MyJourney.Api.Monitoring;

/// <summary>
/// Hängt sich als zusätzlicher Logging-Provider ein und reicht jede Logzeile an den
/// <see cref="LokiShipper"/> weiter. Die normalen Filterregeln aus der Logging-Konfiguration
/// greifen vorher wie gewohnt. Kategorie und Exception stehen in der Zeile, nicht in Labels.
/// </summary>
public sealed class LokiLoggerProvider(LokiShipper shipper) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LokiLogger(shipper, categoryName);

    public void Dispose()
    {
    }

    internal static string MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warning",
        _ => "error",
    };

    internal static string FormatLine(string category, string message, Exception? exception)
        => exception is null
            ? $"[{category}] {message}"
            : $"[{category}] {message}{Environment.NewLine}{exception}";

    private sealed class LokiLogger(LokiShipper shipper, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var line = FormatLine(category, formatter(state, exception), exception);
            shipper.Enqueue(DateTimeOffset.UtcNow, MapLevel(logLevel), line);
        }
    }
}
