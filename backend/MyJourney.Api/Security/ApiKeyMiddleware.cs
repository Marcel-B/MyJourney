namespace MyJourney.Api.Security;

/// <summary>
/// Schützt /api und /mcp per API-Key (Header "X-Api-Key" oder "Authorization: Bearer ...").
/// Sind keine Keys konfiguriert (Security:ApiKeys), ist der Zugriff offen – gedacht für lokale Entwicklung.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyMiddleware> logger)
{
    private static readonly string[] ProtectedPrefixes = ["/api", "/mcp"];

    private readonly HashSet<string> _apiKeys = configuration
        .GetSection("Security:ApiKeys")
        .Get<string[]>()?
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .ToHashSet(StringComparer.Ordinal) ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isProtected = ProtectedPrefixes.Any(p => path.StartsWithSegments(p));

        if (!isProtected || _apiKeys.Count == 0)
        {
            await next(context);
            return;
        }

        var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (providedKey is null)
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authHeader["Bearer ".Length..].Trim();
            }
        }

        if (providedKey is null || !_apiKeys.Contains(providedKey))
        {
            logger.LogWarning("Abgelehnte Anfrage ohne gültigen API-Key auf {Path}", path.Value);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Ungültiger oder fehlender API-Key." });
            return;
        }

        await next(context);
    }
}
