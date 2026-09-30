using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyJourney.Api.Security;

/// <summary>
/// Stellt zustandslose, HMAC-signierte Tokens für den OAuth-Flow aus (Auth-Codes
/// und Access-Tokens). Die Signaturschlüssel werden aus den konfigurierten
/// API-Keys abgeleitet – dadurch braucht es keine neue Tabelle, Tokens überleben
/// Neustarts, und ein Key-Wechsel macht alte Tokens automatisch ungültig.
/// </summary>
public class OAuthTokenService
{
    private const string Prefix = "mj.";

    private readonly List<byte[]> _signingKeys;

    // Bereits eingelöste Auth-Codes (jti -> Ablauf), damit ein Code nur einmal zählt.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _consumedCodes = new();

    public OAuthTokenService(IConfiguration configuration)
    {
        var apiKeys = configuration.GetSection("Security:ApiKeys").Get<string[]>()?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList() ?? [];

        _signingKeys = apiKeys
            .Select(k => SHA256.HashData(Encoding.UTF8.GetBytes("MyJourney.OAuth.v1:" + k)))
            .ToList();

        // Der Passwort-Hash des festen Logins dient ebenfalls als Schlüsselmaterial:
        // Login-Sessions überleben so Neustarts auch ohne API-Keys, und ein
        // Passwortwechsel macht alte Sessions ungültig.
        var passwordHash = configuration["Security:Login:PasswordHash"];
        if (!string.IsNullOrWhiteSpace(passwordHash))
        {
            _signingKeys.Add(SHA256.HashData(Encoding.UTF8.GetBytes("MyJourney.Session.v1:" + passwordHash.Trim())));
        }

        // Ohne konfigurierte Keys (lokale Entwicklung) ein zufälliger Prozess-Schlüssel.
        if (_signingKeys.Count == 0)
        {
            _signingKeys.Add(RandomNumberGenerator.GetBytes(32));
        }
    }

    public string Issue(string type, TimeSpan lifetime, Dictionary<string, string>? claims = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["typ"] = type,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds(),
            ["jti"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
        };
        foreach (var (key, value) in claims ?? []) payload[key] = value;

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = HMACSHA256.HashData(_signingKeys[0], payloadBytes);
        return Prefix + Base64Url(payloadBytes) + "." + Base64Url(signature);
    }

    /// <summary>Prüft Signatur, Typ und Ablauf; liefert die Claims oder null.</summary>
    public Dictionary<string, string>? Validate(string token, string expectedType)
    {
        if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var parts = token[Prefix.Length..].Split('.');
        if (parts.Length != 2) return null;

        byte[] payloadBytes, signature;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!_signingKeys.Any(key =>
                CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payloadBytes), signature)))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;
            if (root.GetProperty("typ").GetString() != expectedType) return null;
            if (root.GetProperty("exp").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;

            var claims = new Dictionary<string, string>();
            foreach (var property in root.EnumerateObject())
            {
                claims[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.GetRawText();
            }
            return claims;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Markiert einen Auth-Code als eingelöst; false, wenn er schon verbraucht war.</summary>
    public bool TryConsumeCode(Dictionary<string, string> claims)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var (jti, exp) in _consumedCodes)
        {
            if (exp < now) _consumedCodes.TryRemove(jti, out _);
        }

        var id = claims.GetValueOrDefault("jti") ?? "";
        var expiry = long.TryParse(claims.GetValueOrDefault("exp"), out var e) ? e : now + 300;
        return _consumedCodes.TryAdd(id, expiry);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
