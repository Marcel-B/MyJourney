# Deployment: Proxmox-CT mit Docker und Tailscale

Die App läuft als ein einzelner Docker-Container: Das .NET-Backend liefert das gebaute Vue-Frontend gleich mit aus (Port 8080). Erreichbar gemacht wird sie ausschließlich über Tailscale – es muss kein Port ins Internet geöffnet werden.

## Schnellstart: alles per Script

Auf dem Proxmox-Host reicht ein Befehl – das Script [`deploy/proxmox-create-ct.sh`](deploy/proxmox-create-ct.sh) legt den CT an (Debian 12, unprivilegiert, nesting/keyctl, TUN-Device für Tailscale, statische IP), installiert Docker samt Compose, klont das Repository, erzeugt einen API-Key und startet die App:

```bash
wget https://raw.githubusercontent.com/Marcel-B/MyJourney/main/deploy/proxmox-create-ct.sh
bash proxmox-create-ct.sh
```

Voreingestellt sind IP `192.168.2.76/24`, Gateway `192.168.2.1`, 2 Cores, 4 GB RAM und 16 GB Disk (der Docker-Build braucht beim ersten Mal etwas Luft; danach läuft die App auch mit weniger RAM). Alle Werte lassen sich per Umgebungsvariable überschreiben, z. B. `CTID=120 IP=192.168.2.80/24 bash proxmox-create-ct.sh`. Am Ende zeigt das Script den API-Key und die Befehle, um Tailscale im CT zu aktivieren (Schritt 4).

Die manuellen Schritte darunter beschreiben, was das Script tut.

## 1. Container (CT) auf Proxmox anlegen

- Template: Debian 12/13, unprivilegierter CT reicht.
- Unter **Options → Features** aktivieren: `nesting=1` und `keyctl=1` (nötig für Docker im LXC).
- Für Tailscale braucht der CT ein TUN-Device. Auf dem Proxmox-Host in `/etc/pve/lxc/<CT-ID>.conf` ergänzen und den CT danach neu starten:

```
lxc.cgroup2.devices.allow: c 10:200 rwm
lxc.mount.entry: /dev/net/tun dev/net/tun none bind,create=file
```

## 2. Docker und Tailscale im CT installieren

```bash
apt update && apt install -y curl git
curl -fsSL https://get.docker.com | sh
curl -fsSL https://tailscale.com/install.sh | sh
tailscale up          # Login-Link öffnen und den CT dem Tailnet hinzufügen
```

## 3. App deployen

```bash
git clone https://github.com/Marcel-B/MyJourney.git
cd MyJourney
echo "MYJOURNEY_API_KEY=$(openssl rand -hex 24)" > .env
docker compose up -d --build
```

Der Container bindet nur an `127.0.0.1:8080`, die SQLite-Datenbank liegt persistent unter `./data/myjourney.db`. Der API-Key aus `.env` schützt `/api` und `/mcp`.

### Login für die Web-Oberfläche (optional)

Statt den API-Key in den Browser einzutragen, kann die Oberfläche eine richtige Login-Maske bekommen: ein fester Benutzer, konfiguriert per Umgebungsvariable. Das Passwort wird nur als PBKDF2-Hash gespeichert. Hash erzeugen und beides in die `.env` eintragen:

```bash
docker compose run --rm --no-deps myjourney hash-password 'dein-passwort'
echo "MYJOURNEY_LOGIN_USER=marcel" >> .env
echo "MYJOURNEY_LOGIN_PASSWORD_HASH=<ausgabe-von-oben>" >> .env
docker compose up -d
```

Danach zeigt die App beim Öffnen eine Anmeldemaske; die Sitzung läuft über ein HttpOnly-Cookie mit 30 Tagen Laufzeit, der Login ist auf 5 Versuche pro Minute und IP begrenzt. Der API-Key gilt unverändert weiter – KI-Assistenten und Skripte melden sich nicht über den Login an. Ein Passwortwechsel (neuer Hash in der `.env`) meldet alle offenen Browser-Sitzungen ab.

Soll die App zusätzlich direkt im Heimnetz erreichbar sein (z. B. `http://192.168.2.76:8080`), in `docker-compose.yml` das Port-Mapping auf `"8080:8080"` ändern und `docker compose up -d` erneut ausführen.

## 4. Über Tailscale erreichbar machen

```bash
tailscale serve --bg 8080
```

Damit ist die App unter `https://<ct-name>.<tailnet>.ts.net` für alle Geräte im Tailnet erreichbar – mit automatischem HTTPS-Zertifikat (MagicDNS und HTTPS müssen in der Tailscale-Admin-Konsole aktiviert sein). `tailscale serve status` zeigt die genaue URL.

## 5. KI-Assistenten anbinden

- **MCP (z. B. Claude):** Endpunkt `https://<ct-name>.<tailnet>.ts.net/mcp`, Header `X-Api-Key: <dein-key>`. Das funktioniert von jedem Gerät im Tailnet, z. B.:

  ```bash
  claude mcp add --transport http myjourney https://<ct-name>.<tailnet>.ts.net/mcp \
    --header "X-Api-Key: <dein-key>"
  ```

- **ChatGPT (MCP-Connector):** ChatGPT läuft außerhalb des Tailnets und braucht deshalb einen öffentlichen Zugang (Schritt 1 und 2 unten). Die App bringt einen OAuth-Flow mit, sodass der Connector direkt auf `/mcp` zeigen kann:

  1. Funnel aktivieren und öffentliche URL in die `.env` eintragen (siehe Custom-GPT-Schritte 1–2 unten – die URL ist für den OAuth-Flow Pflicht).
  2. In ChatGPT: **Einstellungen → Connectors → Erweiterte Einstellungen → Developer Mode** aktivieren, dann **Connector erstellen** mit der MCP-Server-URL `https://<ct-name>.<tailnet>.ts.net/mcp` und Authentifizierung **OAuth**.
  3. Beim Verbinden öffnet sich die MyJourney-Freigabeseite: dort den `MYJOURNEY_API_KEY` eingeben und **Zugriff erlauben**. ChatGPT erhält dann ein Bearer-Token mit 30 Tagen Laufzeit (danach einfach neu verbinden). Ein Key-Wechsel in der `.env` macht alle ausgestellten Tokens sofort ungültig.

- **ChatGPT (Custom GPT mit Actions):** Alternative für Konten, die eigene GPTs erstellen können. Der API-Key ist dann Pflicht.

  1. App per Funnel öffentlich freigeben und die öffentliche URL in die `.env` eintragen, damit sie im OpenAPI-Schema steht:

     ```bash
     tailscale funnel --bg 8080
     echo "MYJOURNEY_PUBLIC_URL=https://<ct-name>.<tailnet>.ts.net" >> .env
     docker compose up -d
     ```

  2. In ChatGPT einen eigenen GPT anlegen (Erkunden → GPT erstellen → Konfigurieren) und unter **Actions → Schema importieren** die URL `https://<ct-name>.<tailnet>.ts.net/openapi/v1.json` angeben.

  3. Als Authentifizierung **API Key** mit Typ **Bearer** wählen und den `MYJOURNEY_API_KEY` aus der `.env` eintragen (die App akzeptiert den Key als `Authorization: Bearer …`).

  4. Testen, z. B. mit „Welche Orte stehen auf meiner Wunschliste?" – ChatGPT ruft dann `ListPlaces` auf.

  Hinweis: Der eigene MCP-Connector von ChatGPT (Einstellungen → Connectors) unterstützt nur OAuth oder keine Authentifizierung und kann den API-Key nicht mitschicken – deshalb der Weg über Actions.

## 6. Betrieb

- **Update:** `git pull && docker compose up -d --build` – oder automatisch, siehe unten.
- **Auto-Update:** [`deploy/auto-update.sh`](deploy/auto-update.sh) beobachtet `origin/main` und führt bei neuen Commits selbstständig Pull und Rebuild aus. Als systemd-Dienst einrichten (einmalig im CT):

  ```bash
  cp /opt/myjourney/deploy/myjourney-autoupdate.service /etc/systemd/system/
  systemctl daemon-reload
  systemctl enable --now myjourney-autoupdate
  ```

  Status und Protokoll: `systemctl status myjourney-autoupdate` bzw. `journalctl -u myjourney-autoupdate -f`. Das Prüfintervall (Standard: 300 s) und der Zwangs-Rebuild ohne neue Commits (Standard: alle 7 Tage, damit Sicherheitsupdates der Basis-Images ankommen; `0` = aus) lassen sich über die Umgebungsvariablen `INTERVAL` und `FORCE_REBUILD_DAYS` in der Service-Datei anpassen; `./deploy/auto-update.sh --once` eignet sich alternativ für cron oder einen systemd-Timer.
- **Logs:** `docker compose logs -f`
- **Backup:** die Datei `data/myjourney.db` sichern (Container vorher kurz stoppen oder SQLite-Online-Backup nutzen).
