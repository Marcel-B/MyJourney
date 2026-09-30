using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Json;
using MyJourney.Api.Data;
using MyJourney.Api.Endpoints;
using MyJourney.Api.Security;
using Scalar.AspNetCore;

// "dotnet run -- hash-password <passwort>" erzeugt den Hash für Security:Login:PasswordHash.
if (args is ["hash-password", ..])
{
    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Aufruf: hash-password <passwort>");
        return 1;
    }
    Console.WriteLine(MyJourney.Api.Security.LoginService.HashPassword(args[1]));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<JourneyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Journey") ?? "Data Source=myjourney.db"));

builder.Services.AddOpenApi(options =>
{
    // Öffentliche Basis-URL (z. B. die Tailscale-Funnel-Adresse) ins Schema schreiben,
    // damit z. B. ChatGPT-Actions das Schema direkt importieren können.
    options.AddDocumentTransformer((document, _, _) =>
    {
        var publicUrl = builder.Configuration["PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            document.Servers = [new() { Url = publicUrl.TrimEnd('/') }];
        }
        return Task.CompletedTask;
    });
});

builder.Services.AddSingleton<MyJourney.Api.Security.OAuthTokenService>();
builder.Services.AddSingleton<MyJourney.Api.Security.LoginService>();

// Bremst Passwort-Rateversuche auf dem Login aus: 5 Versuche pro Minute und IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.RateLimitPolicy, context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
            }));
});

builder.Services.AddHttpClient<MyJourney.Api.Services.OverpassClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    // Overpass verlangt einen identifizierenden User-Agent.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MyJourney/1.0 (+https://github.com/Marcel-B/MyJourney)");
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

// MCP-Server: macht die Reisedaten für KI-Assistenten über /mcp zugänglich.
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JourneyDbContext>();
    db.Database.EnsureCreated();

    // EnsureCreated legt nur neue Datenbanken an. Später ergänzte Spalten werden
    // hier idempotent nachgerüstet ("duplicate column" heißt: schon vorhanden).
    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Places\" ADD COLUMN \"Overnight\" INTEGER NOT NULL DEFAULT 0");
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
    {
        // Spalte existiert bereits.
    }
}

app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();

// Im Container liegt das gebaute Frontend in wwwroot und wird direkt mit ausgeliefert.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapScalarApiReference(); // interaktive API-Doku unter /scalar/v1

app.MapAuthEndpoints();
app.MapPlaceEndpoints();
app.MapTripEndpoints();
app.MapImportEndpoints();
app.MapOAuthEndpoints();
app.MapNearbyEndpoints();
app.MapMcp("/mcp");

if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
return 0;
