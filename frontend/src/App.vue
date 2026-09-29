<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Rating from 'primevue/rating'
import SelectButton from 'primevue/selectbutton'
import Tag from 'primevue/tag'
import Toast from 'primevue/toast'
import ToggleSwitch from 'primevue/toggleswitch'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { isAxiosError } from 'axios'

import Password from 'primevue/password'

import MapView from './components/MapView.vue'
import TripsView from './components/TripsView.vue'
import HereDialog from './components/HereDialog.vue'
import PlaceDialog from './components/PlaceDialog.vue'
import { createPlace, deletePlace, fetchPlaces, fetchTrips, importGooglePlaces, markVisited, setApiKey, updatePlace } from './api/places'
import type { Place, PlaceInput, PlaceStatus, Trip } from './types'

const toast = useToast()
const confirm = useConfirm()

const places = ref<Place[]>([])
const loading = ref(false)
const saving = ref(false)
const search = ref('')
const stopoversOnly = ref(false)
const statusFilter = ref<'All' | PlaceStatus>('All')
const dialogVisible = ref(false)
const editingPlace = ref<Place | null>(null)
const apiKeyDialogVisible = ref(false)
const apiKeyInput = ref('')
const importInput = ref<HTMLInputElement | null>(null)
const importing = ref(false)
const trips = ref<Trip[]>([])
const hereDialogVisible = ref(false)
const view = ref<'list' | 'map' | 'trips'>('list')

// „In der Nähe“: Orte im Umkreis um einen Bezugspunkt, aufsteigend nach Entfernung.
interface NearbyCenter {
  label: string
  latitude: number
  longitude: number
}
const nearbyActive = ref(false)
const nearbyRadius = ref(20)
const nearbyCenter = ref<NearbyCenter | null>(null)
const nearbyCenterPlaceId = ref<string | null>(null)
const locating = ref(false)

const viewOptions = [
  { icon: 'pi pi-list', value: 'list', label: 'Liste' },
  { icon: 'pi pi-map', value: 'map', label: 'Karte' },
  { icon: 'pi pi-compass', value: 'trips', label: 'Reisen' },
]

const statusFilterOptions = [
  { label: 'Alle', value: 'All' },
  { label: 'Wunschliste', value: 'Wishlist' },
  { label: 'Besucht', value: 'Visited' },
]

const wishlistCount = computed(() => places.value.filter((p) => p.status === 'Wishlist').length)
const visitedCount = computed(() => places.value.filter((p) => p.status === 'Visited').length)
const stopoverCount = computed(() => places.value.filter((p) => p.isStopoverCandidate).length)

const placesWithCoords = computed(() =>
  places.value
    .filter((p) => p.latitude !== null && p.longitude !== null)
    .sort((a, b) => a.name.localeCompare(b.name, 'de')),
)

// Haversine-Distanz in Kilometern (gleiche Formel wie im Backend).
function distanceKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const rad = Math.PI / 180
  const dLat = (lat2 - lat1) * rad
  const dLon = (lon2 - lon1) * rad
  const a = Math.sin(dLat / 2) ** 2 +
    Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(dLon / 2) ** 2
  return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
}

const nearbyFilterReady = computed(() => nearbyActive.value && nearbyCenter.value !== null)

const filteredPlaces = computed(() => {
  const term = search.value.trim().toLowerCase()
  const base = places.value.filter((p) => {
    if (statusFilter.value !== 'All' && p.status !== statusFilter.value) return false
    if (stopoversOnly.value && !p.isStopoverCandidate) return false
    if (!term) return true
    return [p.name, p.region, p.country, p.notes]
      .some((field) => field?.toLowerCase().includes(term))
  })

  const center = nearbyCenter.value
  if (!nearbyActive.value || !center) return base

  const radius = nearbyRadius.value || 20
  return base
    .filter((p) => p.latitude !== null && p.longitude !== null)
    .map((p) => ({ ...p, distanceKm: distanceKm(center.latitude, center.longitude, p.latitude!, p.longitude!) }))
    .filter((p) => p.distanceKm <= radius)
    .sort((a, b) => a.distanceKm - b.distanceKm)
})

function onCenterPlaceChange(placeId: string | null) {
  const place = placeId ? places.value.find((p) => p.id === placeId) : null
  nearbyCenter.value = place && place.latitude !== null && place.longitude !== null
    ? { label: place.name, latitude: place.latitude, longitude: place.longitude }
    : null
}

function useMyLocation() {
  if (!navigator.geolocation) {
    toast.add({ severity: 'warn', summary: 'Standort nicht verfügbar', detail: 'Dieser Browser unterstützt keine Standortabfrage.', life: 5000 })
    return
  }
  locating.value = true
  navigator.geolocation.getCurrentPosition(
    (position) => {
      locating.value = false
      nearbyCenterPlaceId.value = null
      nearbyCenter.value = {
        label: 'Mein Standort',
        latitude: position.coords.latitude,
        longitude: position.coords.longitude,
      }
    },
    () => {
      locating.value = false
      toast.add({ severity: 'warn', summary: 'Standort nicht ermittelt', detail: 'Bitte Standortfreigabe im Browser erlauben oder einen Ort als Mittelpunkt wählen.', life: 6000 })
    },
    { enableHighAccuracy: true, timeout: 10000 },
  )
}

function formatDistance(km: number): string {
  return km < 1 ? `${Math.round(km * 1000)} m` : `${km.toFixed(1).replace('.', ',')} km`
}

function errorMessage(error: unknown): string {
  if (isAxiosError(error)) {
    if (error.response?.status === 401) return 'API-Key fehlt oder ist ungültig.'
    if (error.response) return `Der Server hat mit Status ${error.response.status} geantwortet.`
    return 'Das Backend ist nicht erreichbar. Läuft dotnet run?'
  }
  return 'Unerwarteter Fehler.'
}

// Bei 401 den Key-Dialog öffnen statt nur einen Fehler-Toast zu zeigen.
function handleUnauthorized(error: unknown): boolean {
  if (isAxiosError(error) && error.response?.status === 401) {
    apiKeyDialogVisible.value = true
    return true
  }
  return false
}

function saveApiKey() {
  if (!apiKeyInput.value.trim()) return
  setApiKey(apiKeyInput.value.trim())
  apiKeyInput.value = ''
  apiKeyDialogVisible.value = false
  loadPlaces()
}

async function loadPlaces() {
  loading.value = true
  try {
    places.value = await fetchPlaces()
    trips.value = await fetchTrips()
  } catch (error) {
    if (!handleUnauthorized(error)) {
      toast.add({ severity: 'error', summary: 'Laden fehlgeschlagen', detail: errorMessage(error), life: 6000 })
    }
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  editingPlace.value = null
  dialogVisible.value = true
}

function openEditDialog(place: Place) {
  editingPlace.value = place
  dialogVisible.value = true
}

async function savePlace(input: PlaceInput) {
  saving.value = true
  try {
    if (editingPlace.value) {
      const updated = await updatePlace(editingPlace.value.id, input)
      places.value = places.value.map((p) => (p.id === updated.id ? updated : p))
      toast.add({ severity: 'success', summary: 'Gespeichert', detail: `„${updated.name}" wurde aktualisiert.`, life: 3000 })
    } else {
      const created = await createPlace(input)
      places.value = [...places.value, created]
      toast.add({ severity: 'success', summary: 'Angelegt', detail: `„${created.name}" steht jetzt auf der Liste.`, life: 3000 })
    }
    dialogVisible.value = false
  } catch (error) {
    if (!handleUnauthorized(error)) {
      toast.add({ severity: 'error', summary: 'Speichern fehlgeschlagen', detail: errorMessage(error), life: 6000 })
    }
  } finally {
    saving.value = false
  }
}

async function markAsVisited(place: Place) {
  try {
    const updated = await markVisited(place.id)
    places.value = places.value.map((p) => (p.id === updated.id ? updated : p))
    toast.add({ severity: 'success', summary: 'Besucht', detail: `„${updated.name}" ist jetzt als besucht markiert.`, life: 3000 })
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Aktion fehlgeschlagen', detail: errorMessage(error), life: 6000 })
  }
}

function confirmDelete(place: Place) {
  confirm.require({
    message: `„${place.name}" wirklich löschen?`,
    header: 'Löschen bestätigen',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Löschen',
    rejectLabel: 'Abbrechen',
    acceptProps: { severity: 'danger' },
    rejectProps: { severity: 'secondary', text: true },
    accept: async () => {
      try {
        await deletePlace(place.id)
        places.value = places.value.filter((p) => p.id !== place.id)
        toast.add({ severity: 'info', summary: 'Gelöscht', detail: `„${place.name}" wurde entfernt.`, life: 3000 })
      } catch (error) {
        toast.add({ severity: 'error', summary: 'Löschen fehlgeschlagen', detail: errorMessage(error), life: 6000 })
      }
    },
  })
}

async function onImportFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  importing.value = true
  try {
    const result = await importGooglePlaces(file)
    await loadPlaces()
    toast.add({
      severity: 'success',
      summary: 'Import abgeschlossen',
      detail: `${result.imported} von ${result.total} Orten importiert${result.updated ? `, ${result.updated} als besucht markiert` : ''}${result.skipped ? `, ${result.skipped} übersprungen (schon vorhanden)` : ''}.`,
      life: 6000,
    })
  } catch (error) {
    if (!handleUnauthorized(error)) {
      const detail = isAxiosError(error) && error.response?.data?.error
        ? String(error.response.data.error)
        : errorMessage(error)
      toast.add({ severity: 'error', summary: 'Import fehlgeschlagen', detail, life: 8000 })
    }
  } finally {
    importing.value = false
  }
}

function formatDate(value: string | null): string {
  if (!value) return '–'
  return new Date(value).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

onMounted(loadPlaces)
</script>

<template>
  <Toast position="top-right" />
  <ConfirmDialog />

  <div class="max-w-6xl mx-auto px-4 py-8 flex flex-col gap-6">
    <header class="flex flex-wrap items-center justify-between gap-4">
      <div>
        <h1 class="text-3xl font-bold flex items-center gap-2 m-0">
          <i class="pi pi-compass text-primary" style="font-size: 1.75rem" />
          MyJourney
        </h1>
        <p class="text-muted-color mt-1 mb-0">Wohnmobil-Ziele sammeln, Besuchtes festhalten, Reisen planen.</p>
      </div>
      <div class="flex gap-2">
        <Button
          label="Google-Maps-Import"
          icon="pi pi-upload"
          severity="secondary"
          outlined
          :loading="importing"
          v-tooltip.bottom="'Takeout-Export: „Gespeicherte Orte“-JSON oder Listen-CSV'"
          @click="importInput?.click()"
        />
        <Button
          label="Hier bin ich"
          icon="pi pi-map-marker"
          severity="secondary"
          outlined
          v-tooltip.bottom="'Aktuellen Ort als besucht speichern'"
          @click="hereDialogVisible = true"
        />
        <Button label="Neues Ziel" icon="pi pi-plus" @click="openCreateDialog" />
      </div>
      <input ref="importInput" type="file" accept=".json,.csv,.geojson" class="hidden" @change="onImportFileSelected" />
    </header>

    <section class="grid grid-cols-1 sm:grid-cols-3 gap-4">
      <div class="rounded-xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-4 flex items-center gap-4">
        <span class="rounded-full bg-primary-100 dark:bg-primary-900 p-3"><i class="pi pi-map-marker text-primary" /></span>
        <div>
          <div class="text-2xl font-semibold">{{ wishlistCount }}</div>
          <div class="text-sm text-muted-color">Wunschziele</div>
        </div>
      </div>
      <div class="rounded-xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-4 flex items-center gap-4">
        <span class="rounded-full bg-green-100 dark:bg-green-900 p-3"><i class="pi pi-check-circle text-green-600 dark:text-green-400" /></span>
        <div>
          <div class="text-2xl font-semibold">{{ visitedCount }}</div>
          <div class="text-sm text-muted-color">Bereits besucht</div>
        </div>
      </div>
      <div class="rounded-xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-4 flex items-center gap-4">
        <span class="rounded-full bg-amber-100 dark:bg-amber-900 p-3"><i class="pi pi-flag text-amber-600 dark:text-amber-400" /></span>
        <div>
          <div class="text-2xl font-semibold">{{ stopoverCount }}</div>
          <div class="text-sm text-muted-color">Zwischenstopp-Kandidaten</div>
        </div>
      </div>
    </section>

    <section class="flex flex-wrap items-center gap-4">
      <SelectButton v-model="view" :options="viewOptions" option-label="label" option-value="value" :allow-empty="false">
        <template #option="{ option }">
          <i :class="option.icon" class="mr-2" />{{ option.label }}
        </template>
      </SelectButton>
      <template v-if="view === 'list'">
        <SelectButton v-model="statusFilter" :options="statusFilterOptions" option-label="label" option-value="value" :allow-empty="false" />
        <IconField class="flex-1 min-w-52">
          <InputIcon class="pi pi-search" />
          <InputText v-model="search" placeholder="Suchen nach Name, Region, Land …" class="w-full" />
        </IconField>
        <div class="flex items-center gap-2">
          <ToggleSwitch v-model="stopoversOnly" input-id="filter-stopover" />
          <label for="filter-stopover" class="text-sm">Nur Zwischenstopps</label>
        </div>
        <div class="flex items-center gap-2">
          <ToggleSwitch v-model="nearbyActive" input-id="filter-nearby" />
          <label for="filter-nearby" class="text-sm">In der Nähe</label>
        </div>
      </template>
    </section>

    <section v-if="view === 'list' && nearbyActive" class="flex flex-wrap items-center gap-3 rounded-xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-3">
      <span class="text-sm text-muted-color">Umkreis um</span>
      <Select
        v-model="nearbyCenterPlaceId"
        :options="placesWithCoords"
        option-label="name"
        option-value="id"
        placeholder="Ort wählen …"
        filter
        show-clear
        class="w-60"
        @update:model-value="onCenterPlaceChange"
      />
      <Button
        label="Mein Standort"
        icon="pi pi-crosshairs"
        severity="secondary"
        outlined
        :loading="locating"
        v-tooltip.bottom="'Aktuelle Position des Browsers als Mittelpunkt verwenden'"
        @click="useMyLocation"
      />
      <div class="flex items-center gap-2">
        <label for="nearby-radius" class="text-sm text-muted-color">Radius</label>
        <InputNumber
          v-model="nearbyRadius"
          input-id="nearby-radius"
          :min="1"
          :max="1000"
          suffix=" km"
          :input-style="{ width: '6rem' }"
        />
      </div>
      <Tag v-if="nearbyCenter" :value="nearbyCenter.label" icon="pi pi-map-marker" severity="info" />
      <span v-else class="text-sm text-muted-color">Bitte einen Mittelpunkt wählen – Ort aus der Liste oder eigener Standort.</span>
    </section>

    <MapView v-if="view === 'map'" :places="places" :trips="trips" />

    <TripsView v-if="view === 'trips'" :trips="trips" :places="places" :loading="loading" @changed="loadPlaces" />

    <HereDialog v-model:visible="hereDialogVisible" :places="places" @saved="loadPlaces" />

    <DataTable
      v-if="view === 'list'"
      :value="filteredPlaces"
      :loading="loading"
      data-key="id"
      paginator
      :rows="10"
      :rows-per-page-options="[10, 25, 50]"
      :sort-field="nearbyFilterReady ? 'distanceKm' : 'name'"
      :sort-order="1"
      class="rounded-xl overflow-hidden border border-surface-200 dark:border-surface-700"
    >
      <template #empty>
        <div v-if="nearbyFilterReady" class="text-center py-10 text-muted-color">
          <i class="pi pi-map-marker block text-4xl mb-3" />
          Keine Orte im Umkreis von {{ nearbyRadius }} km um {{ nearbyCenter?.label }}.
        </div>
        <div v-else class="text-center py-10 text-muted-color">
          <i class="pi pi-map block text-4xl mb-3" />
          Noch keine Einträge. Leg mit „Neues Ziel" dein erstes Wohnmobil-Ziel an!
        </div>
      </template>

      <Column field="name" header="Name" sortable>
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <i :class="data.kind === 'Region' ? 'pi pi-globe' : 'pi pi-map-marker'" class="text-muted-color" />
            <span class="font-medium">{{ data.name }}</span>
          </div>
        </template>
      </Column>
      <Column v-if="nearbyFilterReady" field="distanceKm" header="Entfernung" sortable>
        <template #body="{ data }">
          <span class="font-medium">{{ formatDistance(data.distanceKm) }}</span>
        </template>
      </Column>
      <Column field="region" header="Region" sortable>
        <template #body="{ data }">{{ data.region ?? '–' }}</template>
      </Column>
      <Column field="country" header="Land" sortable>
        <template #body="{ data }">{{ data.country ?? '–' }}</template>
      </Column>
      <Column field="status" header="Status" sortable>
        <template #body="{ data }">
          <Tag
            :value="data.status === 'Visited' ? 'Besucht' : 'Wunschliste'"
            :severity="data.status === 'Visited' ? 'success' : 'info'"
          />
        </template>
      </Column>
      <Column field="overnight" header="Übernachtung" sortable>
        <template #body="{ data }">
          <Tag v-if="data.overnight === 'Stellplatz'" value="Stellplatz" severity="info" />
          <Tag v-else-if="data.overnight === 'Campingplatz'" value="Campingplatz" severity="success" />
          <span v-else class="text-muted-color">–</span>
        </template>
      </Column>
      <Column field="isStopoverCandidate" header="Zwischenstopp" sortable>
        <template #body="{ data }">
          <i v-if="data.isStopoverCandidate" class="pi pi-flag text-amber-500" v-tooltip.top="'Als Zwischenstopp geeignet'" />
          <span v-else class="text-muted-color">–</span>
        </template>
      </Column>
      <Column field="visitedAt" header="Besucht am" sortable>
        <template #body="{ data }">{{ formatDate(data.visitedAt) }}</template>
      </Column>
      <Column field="rating" header="Bewertung" sortable>
        <template #body="{ data }">
          <Rating v-if="data.rating" :model-value="data.rating" readonly />
          <span v-else class="text-muted-color">–</span>
        </template>
      </Column>
      <Column header="" class="w-32">
        <template #body="{ data }">
          <div class="flex gap-1 justify-end">
            <Button
              v-if="data.status === 'Wishlist'"
              icon="pi pi-check"
              severity="success"
              text
              rounded
              v-tooltip.top="'Als besucht markieren'"
              @click="markAsVisited(data)"
            />
            <Button icon="pi pi-pencil" severity="secondary" text rounded v-tooltip.top="'Bearbeiten'" @click="openEditDialog(data)" />
            <Button icon="pi pi-trash" severity="danger" text rounded v-tooltip.top="'Löschen'" @click="confirmDelete(data)" />
          </div>
        </template>
      </Column>
    </DataTable>
  </div>

  <PlaceDialog v-model:visible="dialogVisible" :place="editingPlace" :saving="saving" @save="savePlace" />

  <Dialog v-model:visible="apiKeyDialogVisible" modal header="API-Key erforderlich" class="w-full max-w-md mx-4">
    <p class="mt-0 text-sm text-muted-color">
      Das Backend ist per API-Key geschützt. Bitte den Key eingeben – er wird lokal im Browser gespeichert.
    </p>
    <Password
      v-model="apiKeyInput"
      :feedback="false"
      toggle-mask
      input-class="w-full"
      class="w-full"
      placeholder="API-Key"
      autofocus
      @keyup.enter="saveApiKey"
    />
    <template #footer>
      <Button label="Speichern" icon="pi pi-check" :disabled="!apiKeyInput.trim()" @click="saveApiKey" />
    </template>
  </Dialog>
</template>
