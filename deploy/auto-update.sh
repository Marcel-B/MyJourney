#!/usr/bin/env bash
# Beobachtet origin/<Branch> und aktualisiert die App bei neuen Commits:
# git pull (nur fast-forward) und docker compose up -d --build.
#
# Aufruf:
#   ./auto-update.sh           # Endlosschleife, prüft alle INTERVAL Sekunden
#   ./auto-update.sh --once    # genau eine Prüfung (z. B. für cron/systemd-Timer)
#
# Konfiguration per Umgebungsvariable:
#   REPO_DIR   Pfad zum Repository (Standard: das Verzeichnis über diesem Script)
#   BRANCH     Beobachteter Branch (Standard: main)
#   INTERVAL   Prüfintervall in Sekunden im Schleifenmodus (Standard: 300)
set -euo pipefail

REPO_DIR="${REPO_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
BRANCH="${BRANCH:-main}"
INTERVAL="${INTERVAL:-300}"

log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') $*"
}

update_once() {
    cd "$REPO_DIR"
    git fetch --quiet origin "$BRANCH"

    local local_rev remote_rev
    local_rev=$(git rev-parse HEAD)
    remote_rev=$(git rev-parse "origin/$BRANCH")

    if [ "$local_rev" = "$remote_rev" ]; then
        return 0
    fi

    log "Neuer Stand auf ${BRANCH}: ${local_rev:0:7} -> ${remote_rev:0:7}, aktualisiere …"
    git pull --ff-only origin "$BRANCH"
    docker compose up -d --build
    # Alte, ersetzte Image-Schichten aufräumen, damit die Disk nicht vollläuft.
    docker image prune -f >/dev/null
    log "Update auf ${remote_rev:0:7} abgeschlossen."
}

if [ "${1:-}" = "--once" ]; then
    update_once
    exit 0
fi

log "Beobachte origin/${BRANCH} in ${REPO_DIR} (alle ${INTERVAL}s, Strg+C zum Beenden) …"
while true; do
    if ! update_once; then
        log "Update fehlgeschlagen – nächster Versuch in ${INTERVAL}s."
    fi
    sleep "$INTERVAL"
done
