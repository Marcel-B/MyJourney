export type PlaceKind = 'Place' | 'Region'
export type PlaceStatus = 'Wishlist' | 'Visited'

export interface Place {
  id: string
  name: string
  kind: PlaceKind
  status: PlaceStatus
  region: string | null
  country: string | null
  latitude: number | null
  longitude: number | null
  notes: string | null
  rating: number | null
  visitedAt: string | null
  isStopoverCandidate: boolean
  createdAt: string
  updatedAt: string
}

export interface PlaceInput {
  name: string
  kind: PlaceKind
  status: PlaceStatus
  region: string | null
  country: string | null
  latitude: number | null
  longitude: number | null
  notes: string | null
  rating: number | null
  visitedAt: string | null
  isStopoverCandidate: boolean
}

export interface MarkVisitedInput {
  visitedAt?: string | null
  rating?: number | null
  notes?: string | null
}
