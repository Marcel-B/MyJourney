// Richtungs-Helfer für den Unterwegs-Modus: gleiche Formeln wie im Backend (NearbyPlaces).

/// Anfangspeilung vom Startpunkt zum Zielpunkt in Grad (0–360, 0 = Norden).
export function bearingDeg(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const rad = Math.PI / 180
  const dLon = (lon2 - lon1) * rad
  const y = Math.sin(dLon) * Math.cos(lat2 * rad)
  const x = Math.cos(lat1 * rad) * Math.sin(lat2 * rad) -
    Math.sin(lat1 * rad) * Math.cos(lat2 * rad) * Math.cos(dLon)
  return normalizeHeading(Math.atan2(y, x) / rad)
}

/// Kleinste Winkeldifferenz zweier Richtungen in Grad (über -180 bis einschließlich 180).
export function angleDeltaDeg(a: number, b: number): number {
  let delta = (a - b) % 360
  if (delta > 180) delta -= 360
  if (delta <= -180) delta += 360
  return delta
}

export function normalizeHeading(deg: number): number {
  const normalized = deg % 360
  return normalized < 0 ? normalized + 360 : normalized
}

const COMPASS_POINTS = ['N', 'NO', 'O', 'SO', 'S', 'SW', 'W', 'NW'] as const

/// Himmelsrichtung als Kurzzeichen, z. B. 312° → "NW".
export function compassPoint(deg: number): string {
  const index = Math.round(normalizeHeading(deg) / 45) % 8
  return COMPASS_POINTS[index]
}

/// Anzeige wie "NW (312°)".
export function formatHeading(deg: number): string {
  const rounded = Math.round(normalizeHeading(deg))
  return `${compassPoint(rounded)} (${rounded % 360}°)`
}

/// Haversine-Distanz in Metern (gleiche Formel wie im Backend).
export function distanceMeters(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const rad = Math.PI / 180
  const dLat = (lat2 - lat1) * rad
  const dLon = (lon2 - lon1) * rad
  const a = Math.sin(dLat / 2) ** 2 +
    Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(dLon / 2) ** 2
  return 6371000 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
}
