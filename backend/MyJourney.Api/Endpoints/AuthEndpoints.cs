using Microsoft.EntityFrameworkCore;
using MyJourney.Api.Data;
using MyJourney.Api.Models;
using MyJourney.Api.Security;

namespace MyJourney.Api.Endpoints;

/// <summary>
/// Login für die Web-Oberfläche: prüft Benutzername und Passwort gegen die
/// Konfiguration (Security:Login) oder gegen per Einladung angelegte Benutzer
/// und legt bei Erfolg eine Cookie-Session an (HttpOnly, SameSite=Strict).
/// Dazu kommen Einladelinks: Angemeldete können einen Link erzeugen, über den
/// sich z. B. der Partner einen eigenen Zugang auf denselben Daten anlegt.
/// Der API-Key-Schutz für KI und Skripte bleibt davon unberührt.
/// </summary>
public static class AuthEndpoints
{
    public const string SessionCookie = "mj_session";
    public const string RateLimitPolicy = "login";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public record LoginRequest(string? Username, string? Password);

    public record AcceptInviteRequest(string? Username, string? Password);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/session", (HttpContext ctx, LoginService login, OAuthTokenService tokens) =>
        {
            var claims = CurrentSession(ctx, tokens);
            return Results.Json(new
            {
                loginConfigured = login.IsConfigured,
                authenticated = claims is not null,
                username = claims?.GetValueOrDefault("sub"),
            });
        }).ExcludeFromDescription();

        app.MapPost("/api/auth/login", async (HttpContext ctx, LoginRequest request, LoginService login, OAuthTokenService tokens, JourneyDbContext db) =>
        {
            if (!login.IsConfigured)
                return Results.BadRequest(new { error = "Es ist kein Login konfiguriert." });

            string? authenticatedAs = null;
            if (login.Verify(request.Username, request.Password))
            {
                authenticatedAs = login.Username;
            }
            else if (!string.IsNullOrWhiteSpace(request.Username) && !string.IsNullOrEmpty(request.Password))
            {
                // Per Einladung angelegte Benutzer liegen in der Datenbank.
                var username = request.Username.Trim().ToLowerInvariant();
                var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.Username.ToLower() == username);
                if (account is not null && LoginService.VerifyPassword(request.Password, account.PasswordHash))
                {
                    authenticatedAs = account.Username;
                }
            }

            if (authenticatedAs is null)
                return Results.Json(new { error = "Benutzername oder Passwort stimmt nicht." },
                    statusCode: StatusCodes.Status401Unauthorized);

            IssueSessionCookie(ctx, tokens, authenticatedAs);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();

        app.MapPost("/api/auth/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).ExcludeFromDescription();

        // Einladelink erzeugen: nur mit gültiger Session. Der Link selbst ist
        // 7 Tage gültig und genau einmal einlösbar.
        app.MapPost("/api/auth/invites", async (HttpContext ctx, LoginService login, OAuthTokenService tokens, JourneyDbContext db) =>
        {
            if (!login.IsConfigured)
                return Results.BadRequest(new { error = "Einladungen brauchen einen konfigurierten Login." });
            if (CurrentSession(ctx, tokens) is null)
                return Results.Json(new { error = "Bitte zuerst anmelden." },
                    statusCode: StatusCodes.Status401Unauthorized);

            var invite = new Invite
            {
                Id = Guid.NewGuid(),
                Token = InviteRules.NewToken(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(InviteRules.Lifetime),
            };
            db.Invites.Add(invite);
            await db.SaveChangesAsync();

            return Results.Ok(new { token = invite.Token, expiresAt = invite.ExpiresAt });
        }).ExcludeFromDescription();

        // Status einer Einladung – für die Einlöse-Seite, daher ohne Anmeldung.
        app.MapGet("/api/auth/invites/{token}", async (string token, JourneyDbContext db) =>
        {
            var invite = await db.Invites.FirstOrDefaultAsync(i => i.Token == token);
            var error = InviteError(invite);
            return Results.Json(new { valid = error is null, error });
        }).ExcludeFromDescription();

        // Einladung einlösen: legt den Benutzer an und meldet ihn direkt an.
        app.MapPost("/api/auth/invites/{token}/accept", async (HttpContext ctx, string token, AcceptInviteRequest request, LoginService login, OAuthTokenService tokens, JourneyDbContext db) =>
        {
            var invite = await db.Invites.FirstOrDefaultAsync(i => i.Token == token);
            if (InviteError(invite) is { } inviteError)
                return Results.BadRequest(new { error = inviteError });

            var (username, usernameError) = InviteRules.ValidateUsername(request.Username);
            if (usernameError is not null)
                return Results.BadRequest(new { error = usernameError });
            if (InviteRules.ValidatePassword(request.Password) is { } passwordError)
                return Results.BadRequest(new { error = passwordError });

            var lowered = username!.ToLowerInvariant();
            var taken = string.Equals(login.Username, username, StringComparison.OrdinalIgnoreCase)
                || await db.UserAccounts.AnyAsync(u => u.Username.ToLower() == lowered);
            if (taken)
                return Results.BadRequest(new { error = "Dieser Benutzername ist schon vergeben." });

            db.UserAccounts.Add(new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = LoginService.HashPassword(request.Password!),
                CreatedAt = DateTime.UtcNow,
            });
            invite!.UsedAt = DateTime.UtcNow;
            invite.UsedByUsername = username;
            await db.SaveChangesAsync();

            IssueSessionCookie(ctx, tokens, username);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();

        return app;
    }

    private static string? InviteError(Invite? invite)
    {
        if (invite is null) return "Diese Einladung gibt es nicht.";
        if (invite.UsedAt is not null) return "Diese Einladung wurde schon eingelöst.";
        if (invite.ExpiresAt < DateTime.UtcNow) return "Diese Einladung ist abgelaufen.";
        return null;
    }

    private static Dictionary<string, string>? CurrentSession(HttpContext ctx, OAuthTokenService tokens)
    {
        var cookie = ctx.Request.Cookies[SessionCookie];
        return cookie is null ? null : tokens.Validate(cookie, "session");
    }

    private static void IssueSessionCookie(HttpContext ctx, OAuthTokenService tokens, string username)
    {
        var session = tokens.Issue("session", SessionLifetime, new Dictionary<string, string> { ["sub"] = username });
        ctx.Response.Cookies.Append(SessionCookie, session, new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            MaxAge = SessionLifetime,
            Path = "/",
        });
    }
}
