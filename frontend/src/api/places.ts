import axios from 'axios'
import type { ImportResult, MarkVisitedInput, NearbyPoi, Place, PlaceInput, PlaceKind, PlaceStatus, Trip, TripImportResult, TripInput } from '../types'

const API_KEY_STORAGE = 'myjourney.apiKey'

export const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? ''

// Jede Browser-Instanz bekommt eine zufällige Id und schickt sie bei allen Anfragen mit.
// Der Live-Stream (/api/events) meldet sie als "origin" zurück, sodass ein Client seine
// eigenen Änderungen wiedererkennt und nicht unnötig neu lädt.
export const clientId: string = (() => {
  try {
    return crypto.randomUUID()
  } catch {
    return `c-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`
  }
})()

const client = axios.create({
  baseURL: apiBaseUrl,
})

// Der Key kommt aus dem localStorage (über den Dialog in der App gesetzt)
// oder als Fallback aus der Build-Umgebung (VITE_API_KEY).
client.interceptors.request.use((config) => {
  const key = getApiKey()
  if (key) config.headers['X-Api-Key'] = key
  config.headers['X-Client-Id'] = clientId
  return config
})

let sessionApiKey: string | null = null

export function getApiKey(): string | null {
  if (sessionApiKey) return sessionApiKey
  try {
    return localStorage.getItem(API_KEY_STORAGE) || import.meta.env.VITE_API_KEY || null
  } catch {
    return import.meta.env.VITE_API_KEY || null
  }
}

export function setApiKey(key: string): void {
  sessionApiKey = key
  try {
    localStorage.setItem(API_KEY_STORAGE, key)
  } catch {
    // localStorage nicht verfügbar (z. B. Private Mode) – der Key gilt dann nur für diese Sitzung.
  }
}

export interface PlaceFilter {
  status?: PlaceStatus
  kind?: PlaceKind
  search?: string
  region?: string
  stopoversOnly?: boolean
}

export async function fetchPlaces(filter: PlaceFilter = {}): Promise<Place[]> {
  const { data } = await client.get<Place[]>('/api/places', { params: filter })
  return data
}

export async function createPlace(input: PlaceInput): Promise<Place> {
  const { data } = await client.post<Place>('/api/places', input)
  return data
}

export async function updatePlace(id: string, input: PlaceInput): Promise<Place> {
  const { data } = await client.put<Place>(`/api/places/${id}`, input)
  return data
}

export async function markVisited(id: string, input: MarkVisitedInput = {}): Promise<Place> {
  const { data } = await client.post<Place>(`/api/places/${id}/visit`, input)
  return data
}

export async function deletePlace(id: string): Promise<void> {
  await client.delete(`/api/places/${id}`)
}

export async function importGooglePlaces(file: File): Promise<ImportResult> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await client.post<ImportResult>('/api/import/google', form)
  return data
}

// ChatGPT-Austausch: Bestand als Datei holen, Prompt-Text laden,
// eine von ChatGPT erzeugte "myjourney-trip"-Datei importieren.
export async function fetchExchangeExport(): Promise<Blob> {
  const { data } = await client.get<Blob>('/api/exchange/export', { responseType: 'blob' })
  return data
}

export async function fetchChatGptPrompt(): Promise<string> {
  const { data } = await client.get<string>('/api/exchange/chatgpt-prompt', {
    responseType: 'text',
    transformResponse: [(raw) => raw],
  })
  return data
}

export async function importTripFile(fileContent: unknown): Promise<TripImportResult> {
  const { data } = await client.post<TripImportResult>('/api/import/trip', fileContent)
  return data
}

export async function fetchTrips(): Promise<Trip[]> {
  const { data } = await client.get<Trip[]>('/api/trips')
  return data
}

export async function createTrip(input: TripInput): Promise<Trip> {
  const { data } = await client.post<Trip>('/api/trips', input)
  return data
}

export async function updateTrip(id: string, input: TripInput): Promise<Trip> {
  const { data } = await client.put<Trip>(`/api/trips/${id}`, input)
  return data
}

export async function deleteTrip(id: string): Promise<void> {
  await client.delete(`/api/trips/${id}`)
}

export async function fetchRegions(): Promise<string[]> {
  const { data } = await client.get<string[]>('/api/regions')
  return data
}

export async function fetchNearby(lat: number, lon: number, radius = 400): Promise<NearbyPoi[]> {
  const { data } = await client.get<NearbyPoi[]>('/api/nearby', { params: { lat, lon, radius } })
  return data
}

// Fester Login: die Session läuft über ein HttpOnly-Cookie, das der Browser
// bei Same-Origin-Anfragen automatisch mitschickt.
export interface AuthSession {
  loginConfigured: boolean
  authenticated: boolean
  username?: string | null
}

export async function fetchAuthSession(): Promise<AuthSession> {
  const { data } = await client.get<AuthSession>('/api/auth/session')
  return data
}

export async function login(username: string, password: string): Promise<void> {
  await client.post('/api/auth/login', { username, password })
}

export async function logout(): Promise<void> {
  await client.post('/api/auth/logout')
}

// Einladelink für den Partner: erzeugen (angemeldet), Status prüfen und
// einlösen (beides ohne Anmeldung, auf der Einlöse-Seite).
export interface CreatedInvite {
  token: string
  expiresAt: string
}

export interface InviteStatus {
  valid: boolean
  error?: string | null
}

export async function createInvite(): Promise<CreatedInvite> {
  const { data } = await client.post<CreatedInvite>('/api/auth/invites')
  return data
}

export async function fetchInviteStatus(token: string): Promise<InviteStatus> {
  const { data } = await client.get<InviteStatus>(`/api/auth/invites/${encodeURIComponent(token)}`)
  return data
}

export async function acceptInvite(token: string, username: string, password: string): Promise<void> {
  await client.post(`/api/auth/invites/${encodeURIComponent(token)}/accept`, { username, password })
}
