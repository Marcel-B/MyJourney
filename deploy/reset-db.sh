#!/usr/bin/env bash
# ACHTUNG: Löscht die komplette SQLite-Datenbank (alle Orte und Reisen)!
# Stoppt den Container, entfernt ./data/myjourney.db und startet neu –
# die App legt beim Start eine leere Datenbank an. Gedacht für den
# Fall, dass man mit einem frischen Stand neu importieren will.
#
# Aufruf im Repo-Verzeichnis (z. B. /opt/myjourney): ./deploy/reset-db.sh
set -euo pipefail

REPO_DIR="${REPO_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
cd "$REPO_DIR"

DB="data/myjourney.db"

if [ ! -f "$DB" ]; then
    echo "Keine Datenbank unter $REPO_DIR/$DB gefunden – nichts zu tun."
    exit 0
fi

echo "Dies löscht ALLE Daten in $REPO_DIR/$DB (Orte, Reisen, Bewertungen)."
read -r -p "Wirklich löschen? [j/N] " answer
case "$answer" in
    j|J|ja|Ja) ;;
    *) echo "Abgebrochen."; exit 1 ;;
esac

# Sicherheitsnetz: letzte Version der Datenbank aufheben.
backup="data/myjourney.db.vor-reset-$(date '+%Y%m%d-%H%M%S')"
docker compose stop
cp "$DB" "$backup"
rm -f "$DB" "$DB-shm" "$DB-wal"
docker compose start

echo "Datenbank gelöscht (Kopie liegt unter $backup)."
echo "Die App legt beim Start eine leere Datenbank an – jetzt kann neu importiert werden."
