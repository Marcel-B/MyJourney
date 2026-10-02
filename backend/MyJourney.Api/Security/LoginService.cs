using System.Security.Cryptography;
using System.Text;

namespace MyJourney.Api.Security;

/// <summary>
/// Fester Login für die Web-Oberfläche: genau ein Benutzer, konfiguriert über
/// Security:Login:Username und Security:Login:PasswordHash (PBKDF2, erzeugt mit
/// "dotnet run -- hash-password"). Ohne Konfiguration bleibt der Login aus und
/// die App verhält sich wie bisher (nur API-Key).
/// </summary>
public class LoginService(IConfiguration configuration)
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly string? _username = Normalize(configuration["Security:Login:Username"]);
    private readonly string? _passwordHash = Normalize(configuration["Security:Login:PasswordHash"]);

    public bool IsConfigured => _username is not null && _passwordHash is not null;

    /// <summary>Der konfigurierte Benutzername (null, wenn kein Login konfiguriert ist).</summary>
    public string? Username => IsConfigured ? _username : null;

    public bool Verify(string? username, string? password)
    {
        if (!IsConfigured || username is null || password is null) return false;

        // Beide Prüfungen laufen immer durch, damit die Antwortzeit nicht verrät,
        // ob schon der Benutzername falsch war.
        var usernameMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(username), Encoding.UTF8.GetBytes(_username!));
        var passwordMatches = VerifyPassword(password, _passwordHash!);
        return usernameMatches && passwordMatches;
    }

    /// <summary>
    /// Erzeugt einen Hash im Format pbkdf2.iterationen.salt.hash (Base64).
    /// Punkt statt "$" als Trenner, damit der Wert unverändert in eine
    /// .env-Datei passt (docker compose interpretiert "$" als Variable).
    /// </summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
