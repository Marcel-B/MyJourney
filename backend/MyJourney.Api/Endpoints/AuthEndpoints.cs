using MyJourney.Api.Security;

namespace MyJourney.Api.Endpoints;

/// <summary>
/// Login für die Web-Oberfläche: prüft Benutzername und Passwort gegen die
/// Konfiguration (Security:Login) und legt bei Erfolg eine Cookie-Session an
/// (HttpOnly, SameSite=Strict). Der API-Key-Schutz für KI und Skripte bleibt
/// davon unberührt.
/// </summary>
public static class AuthEndpoints
{
    public const string SessionCookie = "mj_session";
    public const string RateLimitPolicy = "login";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public record LoginRequest(string? Username, string? Password);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/session", (HttpContext ctx, LoginService login, OAuthTokenService tokens) =>
        {
            var cookie = ctx.Request.Cookies[SessionCookie];
            var authenticated = cookie is not null && tokens.Validate(cookie, "session") is not null;
            return Results.Json(new { loginConfigured = login.IsConfigured, authenticated });
        }).ExcludeFromDescription();

        app.MapPost("/api/auth/login", (HttpContext ctx, LoginRequest request, LoginService login, OAuthTokenService tokens) =>
        {
            if (!login.IsConfigured)
                return Results.BadRequest(new { error = "Es ist kein Login konfiguriert." });

            if (!login.Verify(request.Username, request.Password))
                return Results.Json(new { error = "Benutzername oder Passwort stimmt nicht." },
                    statusCode: StatusCodes.Status401Unauthorized);

            var session = tokens.Issue("session", SessionLifetime);
            ctx.Response.Cookies.Append(SessionCookie, session, new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                MaxAge = SessionLifetime,
                Path = "/",
            });
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();

        app.MapPost("/api/auth/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).ExcludeFromDescription();

        return app;
    }
}
