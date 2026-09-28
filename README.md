# MyJourney 🚐

Eine Anwendung zum Planen von Wohnmobil-Reisen: Orte und Regionen sammeln, die man noch bereisen möchte, besuchte Ziele mit Datum, Bewertung und Notizen festhalten – und die Daten für KI-Assistenten (Claude, ChatGPT, …) zugänglich machen, damit diese bei der Reiseplanung helfen können.

## Tech-Stack

| Bereich  | Technologie |
| -------- | ----------- |
| Frontend | Vue 3 (TypeScript), PrimeVue v4 (Aura-Theme), Tailwind CSS v4, Axios, Vite |
| Backend  | .NET 10 (C#), ASP.NET Core Minimal APIs, EF Core mit SQLite |
| KI-Zugriff | OpenAPI-Dokumentation + MCP-Server-Endpunkt (Model Context Protocol) |

## Projektstruktur

```
backend/MyJourney.Api/   ASP.NET-Core-Backend (REST-API, MCP-Server, SQLite)
frontend/                Vue-3-Frontend (PrimeVue, Tailwind, Axios)
```

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

## KI-Zugriff (MCP)

Das Backend enthält einen MCP-Server unter `/mcp` (Streamable-HTTP-Transport). KI-Assistenten, die MCP unterstützen (z. B. Claude), können damit direkt auf die Reisedaten zugreifen:

| Tool | Beschreibung |
| ---- | ------------ |
| `list_wishlist` | Wunschziele auflisten (optional nach Region/Land gefiltert) |
| `list_visited` | Besuchte Orte mit Datum, Bewertung und Notizen |
| `list_stopover_candidates` | Orte, die sich als Zwischenstopp eignen |
| `search_places` | Freitextsuche über alle Einträge |
| `add_wishlist_place` | Neues Wunschziel hinzufügen |

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

Im Frontend wird der Key über die Umgebungsvariable `VITE_API_KEY` gesetzt (siehe `frontend/.env.example`).
