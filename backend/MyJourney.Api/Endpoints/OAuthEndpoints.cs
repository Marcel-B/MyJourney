using System.Security.Cryptography;
using System.Text;
using MyJourney.Api.Security;

namespace MyJourney.Api.Endpoints;

/// <summary>
/// Minimaler OAuth-2.1-Flow (Authorization Code + PKCE), damit MCP-Clients wie
/// der ChatGPT-Connector /mcp nutzen können, ohne einen statischen API-Key
/// mitschicken zu können. "Login" ist der vorhandene API-Key: Wer ihn im
/// Formular eingibt, bekommt ein Bearer-Token mit begrenzter Laufzeit.
/// </summary>
public static class OAuthEndpoints
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromDays(30);

    private static readonly string[] DefaultRedirectHosts =
    [
        "chatgpt.com", "chat.openai.com", "openai.com",
        "claude.ai", "anthropic.com",
        "localhost", "127.0.0.1",
    ];

    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // RFC 9728: sagt Clients, welcher Authorization Server /mcp schützt.
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx, IConfiguration config) =>
            Results.Json(ProtectedResourceMetadata(BaseUrl(ctx, config))))
            .ExcludeFromDescription();
        app.MapGet("/.well-known/oauth-protected-resource/mcp", (HttpContext ctx, IConfiguration config) =>
            Results.Json(ProtectedResourceMetadata(BaseUrl(ctx, config))))
            .ExcludeFromDescription();

        // RFC 8414: Metadaten des Authorization Servers.
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx, IConfiguration config) =>
        {
            var baseUrl = BaseUrl(ctx, config);
            return Results.Json(new
            {
                issuer = baseUrl,
                authorization_endpoint = $"{baseUrl}/oauth/authorize",
                token_endpoint = $"{baseUrl}/oauth/token",
                registration_endpoint = $"{baseUrl}/oauth/register",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                scopes_supported = new[] { "myjourney" },
            });
        }).ExcludeFromDescription();

        // RFC 7591: Dynamische Client-Registrierung. Es gibt nur öffentliche
        // Clients ohne Secret; die eigentliche Hürde ist der API-Key im Formular.
        app.MapPost("/oauth/register", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();

            string[] redirectUris = [];
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                if (doc.RootElement.TryGetProperty("redirect_uris", out var uris) &&
                    uris.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    redirectUris = uris.EnumerateArray()
                        .Where(u => u.ValueKind == System.Text.Json.JsonValueKind.String)
                        .Select(u => u.GetString()!)
                        .ToArray();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "invalid_client_metadata" });
            }

            return Results.Json(new
            {
                client_id = "mj-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
                client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                redirect_uris = redirectUris,
                token_endpoint_auth_method = "none",
                grant_types = new[] { "authorization_code" },
                response_types = new[] { "code" },
            }, statusCode: StatusCodes.Status201Created);
        }).ExcludeFromDescription();

        app.MapGet("/oauth/authorize", (HttpContext ctx, IConfiguration config) =>
        {
            var query = ctx.Request.Query;
            var error = ValidateAuthorizeRequest(query["response_type"], query["redirect_uri"],
                query["code_challenge"], query["code_challenge_method"], config);
            if (error is not null) return Results.BadRequest(new { error });

            return Results.Content(AuthorizePage(
                redirectUri: query["redirect_uri"]!,
                state: query["state"],
                codeChallenge: query["code_challenge"]!,
                errorMessage: null), "text/html; charset=utf-8");
        }).ExcludeFromDescription();

        app.MapPost("/oauth/authorize", async (HttpContext ctx, IConfiguration config, OAuthTokenService tokens) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            string redirectUri = form["redirect_uri"]!;
            string? state = form["state"];
            string codeChallenge = form["code_challenge"]!;

            var error = ValidateAuthorizeRequest("code", redirectUri, codeChallenge, "S256", config);
            if (error is not null) return Results.BadRequest(new { error });

            var configuredKeys = config.GetSection("Security:ApiKeys").Get<string[]>()?
                .Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.Ordinal) ?? [];
            var providedKey = form["api_key"].ToString();

            if (configuredKeys.Count > 0 && !configuredKeys.Contains(providedKey))
            {
                return Results.Content(AuthorizePage(redirectUri, state, codeChallenge,
                    "Der API-Key stimmt nicht – bitte noch einmal versuchen."), "text/html; charset=utf-8");
            }

            var code = tokens.Issue("code", CodeLifetime, new Dictionary<string, string>
            {
                ["chal"] = codeChallenge,
                ["ruri"] = redirectUri,
            });

            var separator = redirectUri.Contains('?') ? '&' : '?';
            var location = $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrEmpty(state)) location += $"&state={Uri.EscapeDataString(state)}";
            return Results.Redirect(location);
        }).DisableAntiforgery().ExcludeFromDescription();

        app.MapPost("/oauth/token", async (HttpContext ctx, OAuthTokenService tokens) =>
        {
            var form = await ctx.Request.ReadFormAsync();

            if (form["grant_type"] != "authorization_code")
                return TokenError("unsupported_grant_type");

            var claims = tokens.Validate(form["code"].ToString(), "code");
            if (claims is null || !tokens.TryConsumeCode(claims))
                return TokenError("invalid_grant");

            // PKCE: SHA256(code_verifier) muss der beim Authorize hinterlegten Challenge entsprechen.
            var verifier = form["code_verifier"].ToString();
            var computed = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (string.IsNullOrEmpty(verifier) || computed != claims.GetValueOrDefault("chal"))
                return TokenError("invalid_grant");

            var redirectUri = form["redirect_uri"].ToString();
            if (!string.IsNullOrEmpty(redirectUri) && redirectUri != claims.GetValueOrDefault("ruri"))
                return TokenError("invalid_grant");

            var accessToken = tokens.Issue("access", AccessTokenLifetime);
            return Results.Json(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = (long)AccessTokenLifetime.TotalSeconds,
                scope = "myjourney",
            });
        }).DisableAntiforgery().ExcludeFromDescription();

        return app;
    }

    private static object ProtectedResourceMetadata(string baseUrl) => new
    {
        resource = $"{baseUrl}/mcp",
        authorization_servers = new[] { baseUrl },
        bearer_methods_supported = new[] { "header" },
        scopes_supported = new[] { "myjourney" },
    };

    private static IResult TokenError(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Öffentliche Basis-URL: konfiguriert (PublicBaseUrl) oder aus der Anfrage.</summary>
    internal static string BaseUrl(HttpContext ctx, IConfiguration config)
    {
        var configured = config["PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }

    private static string? ValidateAuthorizeRequest(
        string? responseType, string? redirectUri, string? codeChallenge, string? challengeMethod,
        IConfiguration config)
    {
        if (responseType != "code") return "unsupported_response_type";
        if (string.IsNullOrWhiteSpace(codeChallenge) || challengeMethod != "S256") return "invalid_request";
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) return "invalid_request";
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback) return "invalid_request";

        var allowedHosts = config.GetSection("Security:OAuthRedirectHosts").Get<string[]>() ?? DefaultRedirectHosts;
        var host = uri.Host;
        return allowedHosts.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
            ? null
            : "invalid_request";
    }

    private static string AuthorizePage(string redirectUri, string? state, string codeChallenge, string? errorMessage)
    {
        var client = Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) ? uri.Host : "der Client";
        return $$"""
        <!DOCTYPE html>
        <html lang="de">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>MyJourney – Zugriff erlauben</title>
          <style>
            body { font-family: -apple-system, system-ui, sans-serif; background: #f4f4f5; margin: 0;
                   display: grid; place-items: center; min-height: 100vh; }
            .card { background: #fff; border-radius: 12px; padding: 2rem; max-width: 22rem; width: 90%;
                    box-shadow: 0 4px 16px rgba(0,0,0,.08); }
            h1 { font-size: 1.2rem; margin: 0 0 .5rem; }
            p { color: #555; font-size: .9rem; }
            input[type=password] { width: 100%; box-sizing: border-box; padding: .6rem; font-size: 1rem;
                    border: 1px solid #ccc; border-radius: 8px; margin: .5rem 0 1rem; }
            button { width: 100%; padding: .7rem; font-size: 1rem; border: 0; border-radius: 8px;
                     background: #059669; color: #fff; cursor: pointer; }
            .error { color: #dc2626; font-size: .9rem; }
          </style>
        </head>
        <body>
          <form class="card" method="post" action="/oauth/authorize">
            <h1>🧭 MyJourney</h1>
            <p><strong>{{System.Net.WebUtility.HtmlEncode(client)}}</strong> möchte auf deine Reisedaten zugreifen.
               Gib zum Bestätigen deinen API-Key ein.</p>
            {{(errorMessage is null ? "" : $"<p class=\"error\">{System.Net.WebUtility.HtmlEncode(errorMessage)}</p>")}}
            <input type="password" name="api_key" placeholder="API-Key" autofocus autocomplete="off">
            <input type="hidden" name="redirect_uri" value="{{System.Net.WebUtility.HtmlEncode(redirectUri)}}">
            <input type="hidden" name="state" value="{{System.Net.WebUtility.HtmlEncode(state ?? "")}}">
            <input type="hidden" name="code_challenge" value="{{System.Net.WebUtility.HtmlEncode(codeChallenge)}}">
            <button type="submit">Zugriff erlauben</button>
          </form>
        </body>
        </html>
        """;
    }
}
