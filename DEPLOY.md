# Deployment: Proxmox-CT mit Docker und Tailscale

Die App läuft als ein einzelner Docker-Container: Das .NET-Backend liefert das gebaute Vue-Frontend gleich mit aus (Port 8080). Erreichbar gemacht wird sie ausschließlich über Tailscale – es muss kein Port ins Internet geöffnet werden.

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

- **Wichtig:** Gehostete Dienste wie ChatGPT (Custom Actions) laufen außerhalb des Tailnets und erreichen die App so **nicht**. Wenn das gewünscht ist, kann die App gezielt öffentlich freigegeben werden – dann ist der API-Key Pflicht:

  ```bash
  tailscale funnel --bg 8080
  ```

## 6. Betrieb

- **Update:** `git pull && docker compose up -d --build`
- **Logs:** `docker compose logs -f`
- **Backup:** die Datei `data/myjourney.db` sichern (Container vorher kurz stoppen oder SQLite-Online-Backup nutzen).
