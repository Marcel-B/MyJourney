<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { createPlace, fetchNearby, markVisited } from '../api/places'
import type { NearbyPoi, OvernightType, Place } from '../types'

const props = defineProps<{
  visible: boolean
  places: Place[]
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  saved: []
}>()

const toast = useToast()

const step = ref<'locating' | 'choose' | 'error'>('locating')
const errorMessage = ref('')
const coords = ref<{ lat: number; lon: number } | null>(null)
const pois = ref<NearbyPoi[]>([])
const poisLoading = ref(false)
const freeName = ref('')
const saving = ref(false)

/// Eigene Einträge im Umkreis von 2 km, nächste zuerst.
const ownNearby = computed(() => {
  if (!coords.value) return []
  return props.places
    .filter((p) => p.latitude != null && p.longitude != null)
    .map((p) => ({
      place: p,
      distance: haversine(coords.value!.lat, coords.value!.lon, p.latitude!, p.longitude!),
    }))
    .filter((entry) => entry.distance <= 2000)
    .sort((a, b) => a.distance - b.distance)
    .slice(0, 5)
})

/// POIs, die nicht ohnehin schon als eigener Eintrag auftauchen.
const newPois = computed(() => {
  const ownNames = new Set(props.places.map((p) => p.name.toLowerCase()))
  return pois.value.filter((p) => !ownNames.has(p.name.toLowerCase()))
})

const today = () => new Date().toISOString().slice(0, 10)

function haversine(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const rad = Math.PI / 180
  const dLat = (lat2 - lat1) * rad
  const dLon = (lon2 - lon1) * rad
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(dLon / 2) ** 2
  return 6371000 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
}

function formatDistance(meters: number): string {
  return meters < 1000 ? `${Math.round(meters)} m` : `${(meters / 1000).toFixed(1)} km`
}

watch(
  () => props.visible,
  (visible) => {
    if (!visible) return
    step.value = 'locating'
    coords.value = null
    pois.value = []
    freeName.value = ''
    locate()
  },
)

function locate() {
  if (!('geolocation' in navigator)) {
    step.value = 'error'
    errorMessage.value = 'Dein Browser unterstützt keine Standortabfrage.'
    return
  }
  navigator.geolocation.getCurrentPosition(
    async (position) => {
      coords.value = { lat: position.coords.latitude, lon: position.coords.longitude }
      step.value = 'choose'
      poisLoading.value = true
      try {
        pois.value = await fetchNearby(coords.value.lat, coords.value.lon)
      } catch {
        pois.value = []
      } finally {
        poisLoading.value = false
      }
    },
    (error) => {
      step.value = 'error'
      errorMessage.value =
        error.code === error.PERMISSION_DENIED
          ? 'Standortzugriff wurde abgelehnt – bitte in den Browser-Einstellungen erlauben.'
          : 'Der Standort konnte nicht ermittelt werden.'
    },
    { enableHighAccuracy: true, timeout: 15000 },
  )
}

async function markOwnVisited(place: Place) {
  saving.value = true
  try {
    await markVisited(place.id, { visitedAt: today() })
    toast.add({ severity: 'success', summary: 'Hier bin ich', detail: `„${place.name}" ist als besucht markiert.`, life: 4000 })
    emit('saved')
    emit('update:visible', false)
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Speichern fehlgeschlagen', detail: String(error), life: 6000 })
  } finally {
    saving.value = false
  }
}

/// OpenStreetMap-Kategorie auf den Übernachtungstyp abbilden.
function overnightFromCategory(category: string): OvernightType {
  if (category === 'caravan_site') return 'Stellplatz'
  if (category === 'camp_site') return 'Campingplatz'
  return 'None'
}

async function savePoi(poi: NearbyPoi) {
  await saveNew(poi.name, poi.latitude, poi.longitude, overnightFromCategory(poi.category))
}

async function saveFreeName() {
  if (!freeName.value.trim() || !coords.value) return
  await saveNew(freeName.value.trim(), coords.value.lat, coords.value.lon, 'None')
}

async function saveNew(name: string, latitude: number, longitude: number, overnight: OvernightType) {
  saving.value = true
  try {
    await createPlace({
      name,
      kind: 'Place',
      status: 'Visited',
      region: null,
      country: null,
      latitude,
      longitude,
      notes: null,
      rating: null,
      visitedAt: today(),
      isStopoverCandidate: false,
      overnight,
    })
    toast.add({ severity: 'success', summary: 'Hier bin ich', detail: `„${name}" ist als besucht gespeichert.`, life: 4000 })
    emit('saved')
    emit('update:visible', false)
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Speichern fehlgeschlagen', detail: String(error), life: 6000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    header="📍 Hier bin ich"
    class="w-full max-w-lg mx-4"
    @update:visible="emit('update:visible', $event)"
  >
    <div v-if="step === 'locating'" class="py-8 text-center text-muted-color">
      <i class="pi pi-spin pi-spinner text-3xl block mb-3" />
      Standort wird ermittelt …
    </div>

    <div v-else-if="step === 'error'" class="py-6 text-center">
      <i class="pi pi-exclamation-triangle text-3xl block mb-3 text-amber-500" />
      <p class="m-0">{{ errorMessage }}</p>
      <Button label="Erneut versuchen" text class="mt-3" @click="step = 'locating'; locate()" />
    </div>

    <div v-else class="flex flex-col gap-4">
      <template v-if="ownNearby.length > 0">
        <div class="text-sm font-medium">Deine Einträge in der Nähe</div>
        <div
          v-for="entry in ownNearby"
          :key="entry.place.id"
          class="flex items-center gap-3 rounded-lg bg-surface-50 dark:bg-surface-800 px-3 py-2"
        >
          <div class="flex-1 min-w-0">
            <div class="font-medium">{{ entry.place.name }}</div>
            <div class="text-xs text-muted-color">
              {{ formatDistance(entry.distance) }} entfernt
              <Tag
                :value="entry.place.status === 'Visited' ? 'Besucht' : 'Wunschliste'"
                :severity="entry.place.status === 'Visited' ? 'success' : 'info'"
                class="ml-2"
              />
            </div>
          </div>
          <Button
            :label="entry.place.status === 'Visited' ? 'Heute hier' : 'Besucht!'"
            size="small"
            :loading="saving"
            @click="markOwnVisited(entry.place)"
          />
        </div>
      </template>

      <div class="text-sm font-medium">In der Umgebung gefunden</div>
      <div v-if="poisLoading" class="text-sm text-muted-color"><i class="pi pi-spin pi-spinner mr-2" />Umgebung wird durchsucht …</div>
      <div v-else-if="newPois.length === 0" class="text-sm text-muted-color">
        Keine passenden Orte gefunden – benenne den Ort einfach selbst.
      </div>
      <div v-else class="flex flex-col gap-2 max-h-64 overflow-y-auto">
        <div
          v-for="poi in newPois"
          :key="poi.name + poi.latitude"
          class="flex items-center gap-3 rounded-lg bg-surface-50 dark:bg-surface-800 px-3 py-2"
        >
          <div class="flex-1 min-w-0">
            <div class="font-medium">{{ poi.name }}</div>
            <div class="text-xs text-muted-color">{{ formatDistance(poi.distanceMeters) }} · {{ poi.category }}</div>
          </div>
          <Button label="Besucht!" size="small" severity="secondary" :loading="saving" @click="savePoi(poi)" />
        </div>
      </div>

      <div class="flex items-center gap-2 pt-1 border-t border-surface-200 dark:border-surface-700">
        <InputText v-model="freeName" placeholder="Eigener Name für diesen Ort" class="flex-1" @keyup.enter="saveFreeName" />
        <Button label="Speichern" icon="pi pi-check" :disabled="!freeName.trim()" :loading="saving" @click="saveFreeName" />
      </div>
      <p class="text-xs text-muted-color m-0">
        Gespeichert wird mit deinen aktuellen Koordinaten und dem heutigen Datum als Besuchsdatum.
      </p>
    </div>
  </Dialog>
</template>
