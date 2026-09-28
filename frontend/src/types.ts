export type PlaceKind = 'Place' | 'Region'
export type PlaceStatus = 'Wishlist' | 'Visited'
export type OvernightType = 'None' | 'Stellplatz' | 'Campingplatz'

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
  overnight: OvernightType
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
  overnight: OvernightType
}

export interface TripStop {
  id: string
  order: number
  placeId: string | null
  name: string
  latitude: number | null
  longitude: number | null
  notes: string | null
}

export interface TripStopInput {
  placeId?: string | null
  name?: string | null
  latitude?: number | null
  longitude?: number | null
  notes?: string | null
}

export interface TripInput {
  name: string
  notes?: string | null
  startDate?: string | null
  endDate?: string | null
  stops?: TripStopInput[]
}

export interface Trip {
  id: string
  name: string
  notes: string | null
  startDate: string | null
  endDate: string | null
  stops: TripStop[]
  createdAt: string
  updatedAt: string
}

export interface ImportResult {
  total: number
  imported: number
  updated: number
  skipped: number
  importedNames: string[]
  updatedNames: string[]
  skippedNames: string[]
}

export interface MarkVisitedInput {
  visitedAt?: string | null
  rating?: number | null
  notes?: string | null
}

export interface NearbyPoi {
  name: string
  latitude: number
  longitude: number
  category: string
  distanceMeters: number
}
