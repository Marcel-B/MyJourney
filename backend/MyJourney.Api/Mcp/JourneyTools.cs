using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using MyJourney.Api.Contracts;
using MyJourney.Api.Data;
using MyJourney.Api.Models;

namespace MyJourney.Api.Mcp;

/// <summary>
/// MCP-Tools, über die KI-Assistenten (Claude, ChatGPT, …) die Reisedaten
/// abfragen und Wunschziele ergänzen können.
/// </summary>
[McpServerToolType]
public static class JourneyTools
{
    [McpServerTool(Name = "list_wishlist")]
    [Description("Listet alle Orte und Regionen, die noch mit dem Wohnmobil bereist werden sollen (Wunschliste). Optional nach Region oder Land filterbar.")]
    public static async Task<List<PlaceResponse>> ListWishlist(
        JourneyDbContext db,
        [Description("Optionaler Filter auf den Regionsnamen (Teiltreffer genügt).")] string? region = null,
        [Description("Optionaler Filter auf das Land (Teiltreffer genügt).")] string? country = null)
    {
        return await QueryPlaces(db, PlaceStatus.Wishlist, region, country, stopoversOnly: false);
    }

    [McpServerTool(Name = "list_visited")]
    [Description("Listet alle bereits besuchten Orte und Regionen, inklusive Besuchsdatum, Bewertung und Notizen. Optional nach Region oder Land filterbar.")]
    public static async Task<List<PlaceResponse>> ListVisited(
        JourneyDbContext db,
        [Description("Optionaler Filter auf den Regionsnamen (Teiltreffer genügt).")] string? region = null,
        [Description("Optionaler Filter auf das Land (Teiltreffer genügt).")] string? country = null)
    {
        return await QueryPlaces(db, PlaceStatus.Visited, region, country, stopoversOnly: false);
    }

    [McpServerTool(Name = "list_stopover_candidates")]
    [Description("Listet Orte, die sich als Zwischenstopp auf einer längeren Wohnmobilreise eignen. Optional nach Region oder Land filterbar.")]
    public static async Task<List<PlaceResponse>> ListStopoverCandidates(
        JourneyDbContext db,
        [Description("Optionaler Filter auf den Regionsnamen (Teiltreffer genügt).")] string? region = null,
        [Description("Optionaler Filter auf das Land (Teiltreffer genügt).")] string? country = null)
    {
        return await QueryPlaces(db, status: null, region, country, stopoversOnly: true);
    }

    [McpServerTool(Name = "search_places")]
    [Description("Durchsucht alle erfassten Orte und Regionen (Wunschliste und besucht) per Freitext über Name, Region, Land und Notizen.")]
    public static async Task<List<PlaceResponse>> SearchPlaces(
        JourneyDbContext db,
        [Description("Suchbegriff, Teiltreffer genügt.")] string query)
    {
        return await db.Places.AsNoTracking()
            .Where(p =>
                EF.Functions.Like(p.Name, $"%{query}%") ||
                (p.Region != null && EF.Functions.Like(p.Region, $"%{query}%")) ||
                (p.Country != null && EF.Functions.Like(p.Country, $"%{query}%")) ||
                (p.Notes != null && EF.Functions.Like(p.Notes, $"%{query}%")))
            .OrderBy(p => p.Name)
            .Select(p => PlaceResponse.From(p))
            .ToListAsync();
    }

    [McpServerTool(Name = "add_wishlist_place")]
    [Description("Fügt der Wunschliste einen neuen Ort oder eine neue Region hinzu, z. B. wenn bei der Reiseplanung ein neues Ziel auftaucht.")]
    public static async Task<PlaceResponse> AddWishlistPlace(
        JourneyDbContext db,
        [Description("Name des Ortes oder der Region.")] string name,
        [Description("\"Place\" für einen konkreten Ort, \"Region\" für eine ganze Gegend.")] PlaceKind kind = PlaceKind.Place,
        [Description("Region/Gegend, zu der der Ort gehört.")] string? region = null,
        [Description("Land.")] string? country = null,
        [Description("Breitengrad.")] double? latitude = null,
        [Description("Längengrad.")] double? longitude = null,
        [Description("Notizen, z. B. warum das Ziel interessant ist.")] string? notes = null,
        [Description("true, wenn sich der Ort als Zwischenstopp eignet.")] bool isStopoverCandidate = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Der Name darf nicht leer sein.", nameof(name));

        var now = DateTime.UtcNow;
        var place = new Place
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Kind = kind,
            Status = PlaceStatus.Wishlist,
            Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim(),
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsStopoverCandidate = isStopoverCandidate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Places.Add(place);
        await db.SaveChangesAsync();
        return PlaceResponse.From(place);
    }

    private static async Task<List<PlaceResponse>> QueryPlaces(
        JourneyDbContext db,
        PlaceStatus? status,
        string? region,
        string? country,
        bool stopoversOnly)
    {
        var query = db.Places.AsNoTracking();

        if (status is not null) query = query.Where(p => p.Status == status);
        if (stopoversOnly) query = query.Where(p => p.IsStopoverCandidate);
        if (!string.IsNullOrWhiteSpace(region))
            query = query.Where(p => p.Region != null && EF.Functions.Like(p.Region, $"%{region}%"));
        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(p => p.Country != null && EF.Functions.Like(p.Country, $"%{country}%"));

        return await query
            .OrderBy(p => p.Name)
            .Select(p => PlaceResponse.From(p))
            .ToListAsync();
    }
}
