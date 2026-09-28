#!/usr/bin/env bash
# Beobachtet origin/<Branch> und aktualisiert die App bei neuen Commits:
# git pull (nur fast-forward), Build mit frischen Basis-Images (--pull)
# und docker compose up -d. Zusätzlich wird spätestens alle
# FORCE_REBUILD_DAYS Tage neu gebaut, auch ohne neue Commits, damit
# Sicherheitsupdates der Basis-Images regelmäßig ankommen.
#
# Aufruf:
#   ./auto-update.sh           # Endlosschleife, prüft alle INTERVAL Sekunden
#   ./auto-update.sh --once    # genau eine Prüfung (z. B. für cron/systemd-Timer)
#
# Konfiguration per Umgebungsvariable:
#   REPO_DIR             Pfad zum Repository (Standard: das Verzeichnis über diesem Script)
#   BRANCH               Beobachteter Branch (Standard: main)
#   INTERVAL             Prüfintervall in Sekunden im Schleifenmodus (Standard: 300)
#   FORCE_REBUILD_DAYS   Zwangs-Rebuild nach so vielen Tagen ohne Build (Standard: 7, 0 = aus)
set -euo pipefail

REPO_DIR="${REPO_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
BRANCH="${BRANCH:-main}"
INTERVAL="${INTERVAL:-300}"
FORCE_REBUILD_DAYS="${FORCE_REBUILD_DAYS:-7}"

# Merker für den letzten Build; liegt im persistenten data/-Verzeichnis.
STATE_FILE="$REPO_DIR/data/.last-build"

log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') $*"
}

# Alter einer Datei in Sekunden (GNU- und BSD-stat).
file_age_seconds() {
    local mtime
    mtime=$(stat -c %Y "$1" 2>/dev/null || stat -f %m "$1")
    echo $(( $(date +%s) - mtime ))
}

rebuild_due() {
    [ "$FORCE_REBUILD_DAYS" -gt 0 ] || return 1
    [ -f "$STATE_FILE" ] || return 0
    [ "$(file_age_seconds "$STATE_FILE")" -ge $(( FORCE_REBUILD_DAYS * 86400 )) ]
}

rebuild() {
    # --pull zieht aktuelle Basis-Images, damit Sicherheitsupdates mitkommen.
    docker compose build --pull
    docker compose up -d
    # Alte, ersetzte Image-Schichten aufräumen, damit die Disk nicht vollläuft.
    docker image prune -f >/dev/null
    mkdir -p "$(dirname "$STATE_FILE")"
    touch "$STATE_FILE"
}

update_once() {
    cd "$REPO_DIR"
    git fetch --quiet origin "$BRANCH"

    local local_rev remote_rev
    local_rev=$(git rev-parse HEAD)
    remote_rev=$(git rev-parse "origin/$BRANCH")

    if [ "$local_rev" != "$remote_rev" ]; then
        log "Neuer Stand auf ${BRANCH}: ${local_rev:0:7} -> ${remote_rev:0:7}, aktualisiere …"
        git pull --ff-only origin "$BRANCH"
        rebuild
        log "Update auf ${remote_rev:0:7} abgeschlossen."
    elif rebuild_due; then
        log "Kein neuer Commit, aber der letzte Build ist älter als ${FORCE_REBUILD_DAYS} Tage – baue mit frischen Basis-Images neu …"
        rebuild
        log "Zwangs-Rebuild abgeschlossen."
    fi
}

if [ "${1:-}" = "--once" ]; then
    update_once
    exit 0
fi

log "Beobachte origin/${BRANCH} in ${REPO_DIR} (alle ${INTERVAL}s, Zwangs-Rebuild nach ${FORCE_REBUILD_DAYS} Tagen, Strg+C zum Beenden) …"
while true; do
    if ! update_once; then
        log "Update fehlgeschlagen – nächster Versuch in ${INTERVAL}s."
    fi
    sleep "$INTERVAL"
done
