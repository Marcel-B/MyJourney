import axios from 'axios'
import type { MarkVisitedInput, Place, PlaceInput, PlaceKind, PlaceStatus } from '../types'

const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  headers: import.meta.env.VITE_API_KEY
    ? { 'X-Api-Key': import.meta.env.VITE_API_KEY }
    : undefined,
})

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
