using System.Security.Cryptography;

namespace MyJourney.Api.Security;

/// <summary>
/// Regeln rund um Einladungen: Token-Erzeugung und die Prüfung von
/// Benutzername und Passwort bei der Registrierung über einen Einladelink.
/// </summary>
public static class InviteRules
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private const int MinPasswordLength = 8;

    /// <summary>Zufälliges URL-taugliches Token (32 Bytes Entropie).</summary>
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Liefert den normalisierten Benutzernamen oder eine Fehlermeldung.</summary>
    public static (string? Username, string? Error) ValidateUsername(string? raw)
    {
        var username = raw?.Trim();
        if (string.IsNullOrEmpty(username))
            return (null, "Bitte einen Benutzernamen angeben.");
        if (username.Length is < 3 or > 50)
            return (null, "Der Benutzername muss zwischen 3 und 50 Zeichen lang sein.");
        if (username.Any(char.IsWhiteSpace))
            return (null, "Der Benutzername darf keine Leerzeichen enthalten.");
        return (username, null);
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Bitte ein Passwort angeben.";
        if (password.Length < MinPasswordLength)
            return $"Das Passwort muss mindestens {MinPasswordLength} Zeichen lang sein.";
        if (password.Length > 200)
            return "Das Passwort ist zu lang.";
        return null;
    }
}
