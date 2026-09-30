namespace MyJourney.Api.Security;

/// <summary>
/// Schützt /api und /mcp per API-Key (Header "X-Api-Key" oder "Authorization: Bearer ...").
/// Als Bearer werden auch die per OAuth ausgestellten Access-Tokens akzeptiert, und für die
/// Web-Oberfläche zusätzlich die Cookie-Session aus dem festen Login (/api/auth/login).
/// Ist weder ein API-Key noch ein Login konfiguriert, ist der Zugriff offen – gedacht für lokale Entwicklung.
/// </summary>
public class ApiKeyMiddleware(
    RequestDelegate next, IConfiguration configuration, OAuthTokenService tokens, LoginService login,
    ILogger<ApiKeyMiddleware> logger)
{
    private static readonly string[] ProtectedPrefixes = ["/api", "/mcp"];

    // Login und Session-Abfrage müssen ohne Key erreichbar sein; der Login-Endpoint
    // selbst ist per Rate-Limit geschützt.
    private static readonly string[] ExemptPrefixes = ["/api/auth"];

    private readonly HashSet<string> _apiKeys = configuration
        .GetSection("Security:ApiKeys")
        .Get<string[]>()?
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .ToHashSet(StringComparer.Ordinal) ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isProtected = ProtectedPrefixes.Any(p => path.StartsWithSegments(p)) &&
            !ExemptPrefixes.Any(p => path.StartsWithSegments(p));

        if (!isProtected || (_apiKeys.Count == 0 && !login.IsConfigured))
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

        var authorized = providedKey is not null &&
            (_apiKeys.Contains(providedKey) || tokens.Validate(providedKey, "access") is not null);

        // Web-Oberfläche: Cookie-Session aus dem festen Login zählt ebenfalls.
        if (!authorized)
        {
            var sessionCookie = context.Request.Cookies[Endpoints.AuthEndpoints.SessionCookie];
            authorized = sessionCookie is not null && tokens.Validate(sessionCookie, "session") is not null;
        }

        if (!authorized)
        {
            logger.LogWarning("Abgelehnte Anfrage ohne gültigen API-Key auf {Path}", path.Value);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            // RFC 9728: MCP-Clients finden über diesen Hinweis den OAuth-Einstieg.
            var baseUrl = Endpoints.OAuthEndpoints.BaseUrl(context, configuration);
            context.Response.Headers.WWWAuthenticate =
                $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
            await context.Response.WriteAsJsonAsync(new { error = "Ungültiger oder fehlender API-Key." });
            return;
        }

        await next(context);
    }
}
