import { onBeforeUnmount, ref } from 'vue'
import { bearingDeg, distanceMeters, normalizeHeading } from '../heading'

/// Unterhalb ~5 km/h ist der GPS-heading nur Rauschen.
const MIN_SPEED_MS = 1.5
/// Für den Peilungs-Fallback müssen zwei Fixes weit genug auseinanderliegen.
const MIN_MOVE_METERS = 30

/**
 * Unterwegs-Modus: verfolgt die Position laufend (watchPosition) und leitet die
 * Fahrtrichtung ab – bevorzugt aus dem GPS-heading (nur bei Bewegung zuverlässig),
 * sonst als Peilung zwischen den letzten beiden Positionen. Die zuletzt stabile
 * Richtung bleibt auch im Stand erhalten, damit ein direkt nach dem Anhalten
 * gespeicherter Platz noch die Richtung der Anfahrt bekommt.
 */
export function useDrivingMode() {
  const active = ref(false)
  const latitude = ref<number | null>(null)
  const longitude = ref<number | null>(null)
  const speedKmh = ref<number | null>(null)
  const headingDeg = ref<number | null>(null)
  const error = ref<string | null>(null)

  let watchId: number | null = null
  let lastFix: { lat: number; lon: number } | null = null

  function onPosition(position: GeolocationPosition) {
    error.value = null
    const lat = position.coords.latitude
    const lon = position.coords.longitude
    latitude.value = lat
    longitude.value = lon

    const speed = position.coords.speed
    speedKmh.value = speed != null && !Number.isNaN(speed) ? Math.round(speed * 3.6) : null

    const gpsHeading = position.coords.heading
    if (gpsHeading != null && !Number.isNaN(gpsHeading) && speed != null && speed >= MIN_SPEED_MS) {
      headingDeg.value = normalizeHeading(gpsHeading)
      lastFix = { lat, lon }
    } else if (lastFix === null) {
      lastFix = { lat, lon }
    } else if (distanceMeters(lastFix.lat, lastFix.lon, lat, lon) >= MIN_MOVE_METERS) {
      headingDeg.value = bearingDeg(lastFix.lat, lastFix.lon, lat, lon)
      lastFix = { lat, lon }
    }
    // Im Stand: Position aktualisiert, Richtung bleibt die zuletzt stabile.
  }

  function onError(positionError: GeolocationPositionError) {
    if (positionError.code === positionError.PERMISSION_DENIED) {
      error.value = 'Standortzugriff wurde abgelehnt – bitte in den Browser-Einstellungen erlauben.'
      stop()
    } else {
      // Kurze GPS-Aussetzer (z. B. Tunnel) laufen einfach weiter.
      error.value = 'Der Standort ist gerade nicht verfügbar.'
    }
  }

  function start() {
    if (active.value) return
    if (!('geolocation' in navigator)) {
      error.value = 'Dieser Browser unterstützt keine Standortabfrage.'
      return
    }
    error.value = null
    lastFix = null
    active.value = true
    watchId = navigator.geolocation.watchPosition(onPosition, onError, {
      enableHighAccuracy: true,
      maximumAge: 0,
      timeout: 30000,
    })
  }

  function stop() {
    if (watchId !== null) {
      navigator.geolocation.clearWatch(watchId)
      watchId = null
    }
    active.value = false
    // Richtung und Position bleiben stehen – nützlich beim Speichern kurz danach.
  }

  onBeforeUnmount(stop)

  return { active, latitude, longitude, speedKmh, headingDeg, error, start, stop }
}
