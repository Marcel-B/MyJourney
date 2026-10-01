import axios from 'axios'
import type { ImportResult, MarkVisitedInput, NearbyPoi, Place, PlaceInput, PlaceKind, PlaceStatus, Trip, TripImportResult, TripInput } from '../types'

const API_KEY_STORAGE = 'myjourney.apiKey'

const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
})

// Der Key kommt aus dem localStorage (über den Dialog in der App gesetzt)
// oder als Fallback aus der Build-Umgebung (VITE_API_KEY).
client.interceptors.request.use((config) => {
  const key = getApiKey()
  if (key) config.headers['X-Api-Key'] = key
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
