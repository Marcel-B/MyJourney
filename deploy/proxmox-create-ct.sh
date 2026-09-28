#!/usr/bin/env bash
#
# Legt auf einem Proxmox-Host einen LXC-Container für MyJourney an,
# installiert Docker + Compose und startet die App.
#
# Aufruf (als root auf dem Proxmox-Host):
#   bash proxmox-create-ct.sh
#
# Alle Werte lassen sich per Umgebungsvariable überschreiben, z. B.:
#   CTID=120 STORAGE=local-zfs bash proxmox-create-ct.sh
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Konfiguration
# ---------------------------------------------------------------------------
CTID="${CTID:-$(pvesh get /cluster/nextid)}"
HOSTNAME="${HOSTNAME_CT:-myjourney}"
STORAGE="${STORAGE:-local-lvm}"          # Storage für die CT-Disk
TEMPLATE_STORAGE="${TEMPLATE_STORAGE:-local}"  # Storage für das Template
BRIDGE="${BRIDGE:-vmbr0}"
IP="${IP:-192.168.2.76/24}"
GATEWAY="${GATEWAY:-192.168.2.1}"
CORES="${CORES:-2}"
MEMORY="${MEMORY:-4096}"                 # MB – der Docker-Build (npm + dotnet) braucht Luft
SWAP="${SWAP:-512}"                      # MB
DISK="${DISK:-16}"                       # GB – Images (node, dotnet-sdk, aspnet) + Layer
REPO_URL="${REPO_URL:-https://github.com/Marcel-B/MyJourney.git}"

echo "==> Lege CT ${CTID} (${HOSTNAME}) an: ${IP} via ${BRIDGE}, ${CORES} Cores, ${MEMORY} MB RAM, ${DISK} GB Disk"

# ---------------------------------------------------------------------------
# Debian-12-Template besorgen
# ---------------------------------------------------------------------------
pveam update >/dev/null
TEMPLATE="$(pveam available --section system | awk '{print $2}' | grep '^debian-12-standard' | sort -V | tail -1)"
if [[ -z "${TEMPLATE}" ]]; then
    echo "FEHLER: Kein debian-12-standard-Template in 'pveam available' gefunden." >&2
    exit 1
fi
if ! pveam list "${TEMPLATE_STORAGE}" | grep -q "${TEMPLATE}"; then
    echo "==> Lade Template ${TEMPLATE} herunter"
    pveam download "${TEMPLATE_STORAGE}" "${TEMPLATE}"
fi

# ---------------------------------------------------------------------------
# CT anlegen (unprivilegiert, nesting + keyctl für Docker)
# ---------------------------------------------------------------------------
pct create "${CTID}" "${TEMPLATE_STORAGE}:vztmpl/${TEMPLATE}" \
    --hostname "${HOSTNAME}" \
    --unprivileged 1 \
    --features nesting=1,keyctl=1 \
    --cores "${CORES}" \
    --memory "${MEMORY}" \
    --swap "${SWAP}" \
    --rootfs "${STORAGE}:${DISK}" \
    --net0 "name=eth0,bridge=${BRIDGE},ip=${IP},gw=${GATEWAY}" \
    --onboot 1 \
    --start 0

# TUN-Device für Tailscale durchreichen
CONF="/etc/pve/lxc/${CTID}.conf"
{
    echo "lxc.cgroup2.devices.allow: c 10:200 rwm"
    echo "lxc.mount.entry: /dev/net/tun dev/net/tun none bind,create=file"
} >> "${CONF}"

pct start "${CTID}"
echo "==> Warte, bis der CT hochgefahren ist und Netz hat"
for _ in $(seq 1 30); do
    if pct exec "${CTID}" -- getent hosts deb.debian.org >/dev/null 2>&1; then
        break
    fi
    sleep 2
done

# ---------------------------------------------------------------------------
# Docker, Compose und die App im CT einrichten
# ---------------------------------------------------------------------------
echo "==> Installiere Pakete und Docker im CT"
pct exec "${CTID}" -- bash -euo pipefail -c '
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y curl git ca-certificates openssl
    curl -fsSL https://get.docker.com | sh
    systemctl enable --now docker
'

echo "==> Klone das Repository und starte die App"
pct exec "${CTID}" -- bash -euo pipefail -c "
    cd /opt
    git clone '${REPO_URL}' myjourney
    cd myjourney
    echo \"MYJOURNEY_API_KEY=\$(openssl rand -hex 24)\" > .env
    docker compose up -d --build
"

API_KEY="$(pct exec "${CTID}" -- cat /opt/myjourney/.env)"

cat <<SUMMARY

============================================================
 MyJourney läuft im CT ${CTID} (${HOSTNAME})
============================================================
 App (nur im CT selbst):   http://127.0.0.1:8080
 ${API_KEY}
 (steht in /opt/myjourney/.env im CT)

 Nächste Schritte für den Zugriff über Tailscale:
   pct enter ${CTID}
   curl -fsSL https://tailscale.com/install.sh | sh
   tailscale up          # Login-Link öffnen
   tailscale serve --bg 8080
 Danach ist die App unter https://${HOSTNAME}.<tailnet>.ts.net
 erreichbar ('tailscale serve status' zeigt die genaue URL).

 Update später:
   pct exec ${CTID} -- bash -c 'cd /opt/myjourney && git pull && docker compose up -d --build'
============================================================
SUMMARY
