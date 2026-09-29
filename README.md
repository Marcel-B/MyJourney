# MyJourney 🚐

Eine Anwendung zum Planen von Wohnmobil-Reisen: Orte und Regionen sammeln, die man noch bereisen möchte, besuchte Ziele mit Datum, Bewertung und Notizen festhalten – und die Daten für KI-Assistenten (Claude, ChatGPT, …) zugänglich machen, damit diese bei der Reiseplanung helfen können.

## Tech-Stack

| Bereich  | Technologie |
| -------- | ----------- |
| Frontend | Vue 3 (TypeScript), PrimeVue v4 (Aura-Theme), Tailwind CSS v4, Axios, Leaflet (OpenStreetMap), Vite |
| Backend  | .NET 10 (C#), ASP.NET Core Minimal APIs, EF Core mit SQLite |
| KI-Zugriff | OpenAPI-Dokumentation + MCP-Server-Endpunkt (Model Context Protocol) |

## Projektstruktur

```
backend/MyJourney.Api/   ASP.NET-Core-Backend (REST-API, MCP-Server, SQLite)
frontend/                Vue-3-Frontend (PrimeVue, Tailwind, Axios)
Dockerfile               Ein Image für alles: Backend liefert das Frontend mit aus
docker-compose.yml       Betrieb mit persistenter SQLite-DB und API-Key
```

## Deployment

Für den Betrieb auf Proxmox (Docker in einem LXC-Container) mit Zugriff über Tailscale siehe [DEPLOY.md](DEPLOY.md). Kurzfassung: `docker compose up -d --build`, dann `tailscale serve --bg 8080`.

## Entwicklung starten

### Backend

```bash
cd backend/MyJourney.Api
dotnet run --launch-profile http    # läuft auf http://localhost:5032
```

Beim ersten Start wird automatisch die SQLite-Datenbank `myjourney.db` angelegt.

- **OpenAPI-Spezifikation:** http://localhost:5032/openapi/v1.json
- **Interaktive API-Doku (Scalar):** http://localhost:5032/scalar/v1
- **MCP-Endpunkt:** http://localhost:5032/mcp (Streamable HTTP)

### Frontend

```bash
cd frontend
npm install
npm run dev                         # läuft auf http://localhost:5173
```

Der Vite-Dev-Server leitet `/api` und `/mcp` an das Backend auf Port 5032 weiter (siehe `vite.config.ts`), es ist also keine weitere Konfiguration nötig. Alternativ kann über `.env` (Vorlage: `.env.example`) eine andere Backend-URL und ein API-Key gesetzt werden.

## Datenmodell

Ein Eintrag (`Place`) ist entweder ein konkreter **Ort** oder eine ganze **Region** (`kind`) und hat einen Status:

- **Wishlist** – da möchte ich noch hin
- **Visited** – da war ich schon (mit Besuchsdatum, Bewertung 1–5 und Notizen)

Zusätzlich kann jeder Eintrag als **Zwischenstopp-Kandidat** markiert werden – nützlich, wenn ein KI-Assistent eine Route plant und Stopps vorschlagen soll. Koordinaten (Breiten-/Längengrad) sind optional.

## REST-API

| Methode | Pfad | Beschreibung |
| ------- | ---- | ------------ |
| GET | `/api/places` | Einträge auflisten (Filter: `status`, `kind`, `search`, `region`, `stopoversOnly`) |
| GET | `/api/places/{id}` | Einzelnen Eintrag abrufen |
| POST | `/api/places` | Eintrag anlegen |
| PUT | `/api/places/{id}` | Eintrag aktualisieren |
| POST | `/api/places/{id}/visit` | Eintrag als besucht markieren |
| DELETE | `/api/places/{id}` | Eintrag löschen |
| GET | `/api/regions` | Alle erfassten Regionsnamen |
| POST | `/api/import/google` | Google-Takeout-Datei importieren (Multipart-Upload) |
| GET | `/api/trips` | Reisen mit ihren Stopps auflisten |
| GET | `/api/trips/{id}` | Eine Reise abrufen |
| POST | `/api/trips` | Reise mit geordneten Stopps anlegen |
| PUT | `/api/trips/{id}` | Reise aktualisieren (ersetzt auch die Stopps) |
| DELETE | `/api/trips/{id}` | Reise löschen |

## Karte und geplante Reisen

Die Ansicht **Karte** zeigt alle Einträge mit Koordinaten auf einer OpenStreetMap-Karte (blau = Wunschliste, grün = besucht). Über die Reise-Auswahl lässt sich eine geplante Route mit nummerierten Stopps einblenden.

Eine Reise (`Trip`) besteht aus geordneten Stopps: Jeder Stopp verweist entweder per `placeId` auf einen erfassten Ort oder bringt einen eigenen Namen mit Koordinaten mit. So kann ein KI-Assistent per `create_trip` (MCP) oder `POST /api/trips` eine geplante Route ablegen, die anschließend in der App sichtbar ist.

## Google-Maps-Orte importieren

Mit Stern markierte Orte und gespeicherte Listen aus Google Maps lassen sich über den Button **Google-Maps-Import** in der Oberfläche einlesen. Den Export gibt es bei [Google Takeout](https://takeout.google.com):

1. Bei Takeout nur **„Maps (Meine Orte)"** bzw. **„Gespeichert"** auswählen und exportieren.
2. Im heruntergeladenen Archiv liegt `Gespeicherte Orte.json` (bzw. `Saved Places.json`) mit den Sternorten; gespeicherte Listen (z. B. „Favoriten") liegen als CSV-Dateien bei.
3. Die Datei in der App hochladen – die Orte landen auf der Wunschliste, Namen die es schon gibt werden übersprungen. Koordinaten und Notizen werden übernommen, soweit im Export enthalten.

## KI-Zugriff (MCP)

Das Backend enthält einen MCP-Server unter `/mcp` (Streamable-HTTP-Transport). KI-Assistenten, die MCP unterstützen (z. B. Claude), können damit direkt auf die Reisedaten zugreifen:

| Tool | Beschreibung |
| ---- | ------------ |
| `list_wishlist` | Wunschziele auflisten (optional nach Region/Land gefiltert) |
| `list_visited` | Besuchte Orte mit Datum, Bewertung und Notizen |
| `list_stopover_candidates` | Orte, die sich als Zwischenstopp eignen |
| `search_places` | Freitextsuche über alle Einträge |
| `find_places_nearby` | Erfasste Orte im Umkreis (Standard 20 km), aufsteigend nach Entfernung |
| `add_wishlist_place` | Neues Wunschziel hinzufügen |
| `list_trips` | Geplante Reisen mit ihren Stopps auflisten |
| `create_trip` | Geplante Reise mit geordneten Stopps anlegen |

Beispiel-Konfiguration für Claude Code:

```bash
claude mcp add --transport http myjourney http://localhost:5032/mcp \
  --header "X-Api-Key: <dein-key>"
```

Für Assistenten ohne MCP-Unterstützung (z. B. ChatGPT-Actions) dient die OpenAPI-Spezifikation unter `/openapi/v1.json` als Grundlage.

## Absicherung per API-Key

Standardmäßig ist die API offen (lokale Entwicklung). Sobald in `appsettings.json` unter `Security:ApiKeys` mindestens ein Key eingetragen ist, verlangen `/api` und `/mcp` einen gültigen Key – entweder als Header `X-Api-Key: <key>` oder als `Authorization: Bearer <key>`:

```json
{
  "Security": {
    "ApiKeys": ["mein-geheimer-key"]
  }
}
```

Das Frontend fragt den Key beim ersten Zugriff über einen Dialog ab und merkt ihn sich im Browser (localStorage). Alternativ kann er zur Build-Zeit über die Umgebungsvariable `VITE_API_KEY` gesetzt werden (siehe `frontend/.env.example`).
