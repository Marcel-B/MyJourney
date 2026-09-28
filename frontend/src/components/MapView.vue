<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import type { Place, Trip } from '../types'

const props = defineProps<{
  places: Place[]
  trips: Trip[]
}>()

const mapContainer = ref<HTMLDivElement | null>(null)
const selectedTripId = ref<string | null>(null)

let map: L.Map | null = null
let placesLayer: L.LayerGroup | null = null
let tripLayer: L.LayerGroup | null = null

const statusColors: Record<string, string> = {
  Wishlist: '#3b82f6', // blau – Wunschziel
  Visited: '#22c55e', // grün – besucht
}

function popupHtml(place: Place): string {
  const status = place.status === 'Visited' ? 'Besucht' : 'Wunschliste'
  const parts = [`<strong>${escapeHtml(place.name)}</strong>`, status]
  if (place.region || place.country) {
    parts.push(escapeHtml([place.region, place.country].filter(Boolean).join(', ')))
  }
  if (place.notes) parts.push(escapeHtml(place.notes))
  return parts.join('<br>')
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string,
  )
}

function renderPlaces() {
  if (!map || !placesLayer) return
  placesLayer.clearLayers()

  for (const place of props.places) {
    if (place.latitude == null || place.longitude == null) continue
    L.circleMarker([place.latitude, place.longitude], {
      radius: 8,
      color: statusColors[place.status] ?? '#3b82f6',
      fillColor: statusColors[place.status] ?? '#3b82f6',
      fillOpacity: 0.75,
      weight: 2,
    })
      .bindPopup(popupHtml(place))
      .addTo(placesLayer)
  }
}

function renderTrip() {
  if (!map || !tripLayer) return
  tripLayer.clearLayers()

  const trip = props.trips.find((t) => t.id === selectedTripId.value)
  if (!trip) return

  const points: L.LatLngExpression[] = []
  for (const stop of trip.stops) {
    if (stop.latitude == null || stop.longitude == null) continue
    const latLng: L.LatLngExpression = [stop.latitude, stop.longitude]
    points.push(latLng)

    L.marker(latLng, {
      icon: L.divIcon({
        className: '',
        html: `<div class="trip-stop-marker">${stop.order}</div>`,
        iconSize: [26, 26],
        iconAnchor: [13, 13],
      }),
    })
      .bindPopup(
        `<strong>${stop.order}. ${escapeHtml(stop.name)}</strong>${stop.notes ? '<br>' + escapeHtml(stop.notes) : ''}`,
      )
      .addTo(tripLayer)
  }

  if (points.length >= 2) {
    L.polyline(points, { color: '#f59e0b', weight: 3, dashArray: '8 6' }).addTo(tripLayer)
  }
  if (points.length > 0) {
    map.fitBounds(L.latLngBounds(points).pad(0.25))
  }
}

function fitToPlaces() {
  if (!map) return
  const coords = props.places
    .filter((p) => p.latitude != null && p.longitude != null)
    .map((p) => [p.latitude!, p.longitude!] as [number, number])
  if (coords.length > 0) {
    map.fitBounds(L.latLngBounds(coords).pad(0.25))
  } else {
    map.setView([51.163, 10.447], 5) // Mitte Deutschlands als Ausgangspunkt
  }
}

onMounted(() => {
  if (!mapContainer.value) return
  map = L.map(mapContainer.value)
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
  }).addTo(map)
  placesLayer = L.layerGroup().addTo(map)
  tripLayer = L.layerGroup().addTo(map)

  renderPlaces()
  fitToPlaces()
})

onBeforeUnmount(() => {
  map?.remove()
  map = null
})

watch(() => props.places, renderPlaces, { deep: true })
watch([() => props.trips, selectedTripId], renderTrip, { deep: true })
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex flex-wrap items-center gap-3">
      <Select
        v-model="selectedTripId"
        :options="trips"
        option-label="name"
        option-value="id"
        placeholder="Reise auswählen …"
        show-clear
        class="min-w-64"
      />
      <template v-if="selectedTripId">
        <Tag
          v-for="stop in trips.find((t) => t.id === selectedTripId)?.stops ?? []"
          :key="stop.id"
          :value="`${stop.order}. ${stop.name}`"
          severity="warn"
        />
      </template>
      <div v-else class="text-sm text-muted-color flex items-center gap-4">
        <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 rounded-full" style="background: #3b82f6" /> Wunschliste</span>
        <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 rounded-full" style="background: #22c55e" /> Besucht</span>
      </div>
    </div>
    <div ref="mapContainer" class="h-[600px] rounded-xl border border-surface-200 dark:border-surface-700 overflow-hidden" />
    <p class="text-sm text-muted-color m-0">
      Angezeigt werden alle Einträge mit Koordinaten. Reisen mit Stopps können auch von KI-Assistenten über die MCP-Schnittstelle angelegt werden.
    </p>
  </div>
</template>

<style>
.trip-stop-marker {
  width: 26px;
  height: 26px;
  border-radius: 9999px;
  background: #f59e0b;
  color: #fff;
  font-weight: 700;
  font-size: 13px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 2px solid #fff;
  box-shadow: 0 1px 4px rgb(0 0 0 / 0.4);
}
</style>
