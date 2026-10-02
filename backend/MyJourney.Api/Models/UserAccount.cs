namespace MyJourney.Api.Models;

/// <summary>
/// Zusätzlicher Benutzer, der sich über einen Einladelink registriert hat
/// (z. B. der Partner). Der feste Login aus der Konfiguration bleibt daneben
/// bestehen; beide arbeiten auf demselben Datenbestand.
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }

    public required string Username { get; set; }

    /// <summary>PBKDF2-Hash im selben Format wie beim festen Login.</summary>
    public required string PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Einmal-Einladung: ein Link mit zufälligem Token, über den sich genau eine
/// Person einen eigenen Zugang anlegen kann. Nach dem Einlösen oder Ablauf
/// ist der Link wertlos.
/// </summary>
public class Invite
{
    public Guid Id { get; set; }

    public required string Token { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public string? UsedByUsername { get; set; }
}
