import axios from 'axios'
import type { MarkVisitedInput, Place, PlaceInput, PlaceKind, PlaceStatus } from '../types'

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

export async function fetchRegions(): Promise<string[]> {
  const { data } = await client.get<string[]>('/api/regions')
  return data
}
