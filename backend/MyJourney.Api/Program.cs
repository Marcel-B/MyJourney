using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Json;
using MyJourney.Api.Data;
using MyJourney.Api.Endpoints;
using MyJourney.Api.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<JourneyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Journey") ?? "Data Source=myjourney.db"));

builder.Services.AddOpenApi();

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
}

app.UseCors();
app.UseMiddleware<ApiKeyMiddleware>();

// Im Container liegt das gebaute Frontend in wwwroot und wird direkt mit ausgeliefert.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapScalarApiReference(); // interaktive API-Doku unter /scalar/v1

app.MapPlaceEndpoints();
app.MapMcp("/mcp");

if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
