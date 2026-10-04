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
import Menu from 'primevue/menu'
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

import ChatGptDialog from './components/ChatGptDialog.vue'
import InviteDialog from './components/InviteDialog.vue'
import InviteView from './components/InviteView.vue'
import MapView from './components/MapView.vue'
import TripsView from './components/TripsView.vue'
import HereDialog from './components/HereDialog.vue'
import PlaceDialog from './components/PlaceDialog.vue'
import { createPlace, deletePlace, fetchAuthSession, fetchPlaces, fetchTrips, importGooglePlaces, login, logout, markVisited, setApiKey, updatePlace } from './api/places'
import { useLiveUpdates } from './composables/useLiveUpdates'
import { countryFlag } from './countryFlags'
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

// Fester Login: ist einer konfiguriert, zeigt die App statt des Inhalts eine
// Login-Maske; die Session selbst steckt in einem HttpOnly-Cookie.
const loginConfigured = ref(false)
const loginRequired = ref(false)
const loginUsername = ref('')
const loginPassword = ref('')
const loginError = ref('')
const loggingIn = ref(false)

// Einladelink: /invite?token=… zeigt statt Login/App die Einlöse-Seite.
const inviteToken = ref<string | null>(null)
const inviteDialogVisible = ref(false)
const importInput = ref<HTMLInputElement | null>(null)
const importing = ref(false)
const trips = ref<Trip[]>([])
const hereDialogVisible = ref(false)
const chatGptDialogVisible = ref(false)
const view = ref<'list' | 'map' | 'trips'>('list')

// Am Handy ist nur „Neues Ziel" ein eigener Button; die übrigen Aktionen
// stecken in diesem Popup-Menü, damit die Kopfzeile nicht überläuft.
const actionMenu = ref<InstanceType<typeof Menu> | null>(null)
const actionMenuItems = computed(() => {
  const items: Record<string, unknown>[] = [
    { label: 'Aktualisieren', icon: 'pi pi-refresh', command: () => { loadPlaces() } },
    { label: 'Google-Maps-Import', icon: 'pi pi-upload', command: () => importInput.value?.click() },
    { label: 'ChatGPT', icon: 'pi pi-comments', command: () => { chatGptDialogVisible.value = true } },
    { label: 'Hier bin ich', icon: 'pi pi-map-marker', command: () => { hereDialogVisible.value = true } },
  ]
  if (loginConfigured.value) {
    items.push(
      { separator: true },
      { label: 'Partner einladen', icon: 'pi pi-user-plus', command: () => { inviteDialogVisible.value = true } },
      { label: 'Abmelden', icon: 'pi pi-sign-out', command: () => { doLogout() } },
    )
  }
  return items
})

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

// Bei 401 die Login-Maske (bzw. ohne konfigurierten Login den Key-Dialog)
// öffnen statt nur einen Fehler-Toast zu zeigen.
function handleUnauthorized(error: unknown): boolean {
  if (isAxiosError(error) && error.response?.status === 401) {
    if (loginConfigured.value) {
      loginRequired.value = true
    } else {
      apiKeyDialogVisible.value = true
    }
    return true
  }
  return false
}

async function submitLogin() {
  if (!loginUsername.value.trim() || !loginPassword.value) return
  loggingIn.value = true
  loginError.value = ''
  try {
    await login(loginUsername.value.trim(), loginPassword.value)
    loginRequired.value = false
    loginPassword.value = ''
    await loadPlaces()
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 401) {
      loginError.value = 'Benutzername oder Passwort stimmt nicht.'
    } else if (isAxiosError(error) && error.response?.status === 429) {
      loginError.value = 'Zu viele Versuche – bitte eine Minute warten.'
    } else {
      loginError.value = errorMessage(error)
    }
  } finally {
    loggingIn.value = false
  }
}

async function doLogout() {
  live.stop()
  try {
    await logout()
  } catch {
    // Auch wenn der Aufruf scheitert: zurück zur Login-Maske.
  }
  loginPassword.value = ''
  loginRequired.value = true
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
    live.start()
  } catch (error) {
    if (!handleUnauthorized(error)) {
      toast.add({ severity: 'error', summary: 'Laden fehlgeschlagen', detail: errorMessage(error), life: 6000 })
    }
  } finally {
    loading.value = false
  }
}

// Stilles Neuladen für Live-Updates: ohne Lade-Spinner und ohne Fehler-Toast,
// damit Änderungen des Partners einfach erscheinen und kurze Verbindungs-
// aussetzer keine Meldungen stapeln.
async function reloadPlaces() {
  try {
    const [newPlaces, newTrips] = await Promise.all([fetchPlaces(), fetchTrips()])
    places.value = newPlaces
    trips.value = newTrips
  } catch (error) {
    handleUnauthorized(error)
  }
}

const live = useLiveUpdates(reloadPlaces)

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

// Nach dem ChatGPT-Import direkt zur Reisen-Ansicht wechseln.
async function onTripImported() {
  await loadPlaces()
  view.value = 'trips'
}

function formatDate(value: string | null): string {
  if (!value) return '–'
  return new Date(value).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

// Nach dem Einlösen der Einladung ist die Session schon gesetzt – zurück zur App.
async function onInviteAccepted() {
  inviteToken.value = null
  window.history.replaceState(null, '', '/')
  loginConfigured.value = true
  loginRequired.value = false
  await loadPlaces()
}

onMounted(async () => {
  const token = new URLSearchParams(window.location.search).get('token')
  if (window.location.pathname === '/invite' && token) {
    inviteToken.value = token
    return
  }

  try {
    const session = await fetchAuthSession()
    loginConfigured.value = session.loginConfigured
    if (session.loginConfigured && !session.authenticated) {
      loginRequired.value = true
      return
    }
  } catch {
    // Session-Endpoint nicht erreichbar (z. B. älteres Backend) – normal weiterladen.
  }
  await loadPlaces()
})
</script>

<template>
  <Toast position="top-right" />
  <ConfirmDialog />

  <InviteView v-if="inviteToken" :token="inviteToken" @accepted="onInviteAccepted" />

  <div v-else-if="loginRequired" class="min-h-screen grid place-items-center px-4">
    <form
      class="w-full max-w-sm rounded-xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-8 flex flex-col gap-4"
      @submit.prevent="submitLogin"
    >
      <div class="text-center">
        <i class="pi pi-compass text-primary" style="font-size: 2.5rem" />
        <h1 class="text-2xl font-bold mt-2 mb-1">MyJourney</h1>
        <p class="text-sm text-muted-color m-0">Bitte anmelden, um deine Reisen zu sehen.</p>
      </div>
      <div class="flex flex-col gap-1">
        <label for="login-username" class="text-sm">Benutzername</label>
        <InputText id="login-username" v-model="loginUsername" autocomplete="username" autofocus class="w-full" />
      </div>
      <div class="flex flex-col gap-1">
        <label for="login-password" class="text-sm">Passwort</label>
        <Password
          v-model="loginPassword"
          input-id="login-password"
          :feedback="false"
          toggle-mask
          input-class="w-full"
          class="w-full"
          :input-props="{ autocomplete: 'current-password' }"
        />
      </div>
      <p v-if="loginError" class="text-sm text-red-600 dark:text-red-400 m-0">{{ loginError }}</p>
      <Button
        type="submit"
        label="Anmelden"
        icon="pi pi-sign-in"
        :loading="loggingIn"
        :disabled="!loginUsername.trim() || !loginPassword"
      />
    </form>
  </div>

  <div v-else class="max-w-6xl mx-auto px-4 py-8 flex flex-col gap-6">
    <header class="flex flex-wrap items-center justify-between gap-4">
      <div>
        <h1 class="text-3xl font-bold flex items-center gap-2 m-0">
          <i class="pi pi-compass text-primary" style="font-size: 1.75rem" />
          MyJourney
        </h1>
        <p class="text-muted-color mt-1 mb-0">Wohnmobil-Ziele sammeln, Besuchtes festhalten, Reisen planen.</p>
      </div>
      <div class="flex items-center gap-2">
        <!-- Ab md-Breite: alle Aktionen als eigene Buttons wie bisher. -->
        <Button
          class="hidden md:inline-flex whitespace-nowrap"
          label="Google-Maps-Import"
          icon="pi pi-upload"
          severity="secondary"
          outlined
          :loading="importing"
          v-tooltip.bottom="'Takeout-Export: „Gespeicherte Orte“-JSON oder Listen-CSV'"
          @click="importInput?.click()"
        />
        <Button
          class="hidden md:inline-flex whitespace-nowrap"
          label="ChatGPT"
          icon="pi pi-comments"
          severity="secondary"
          outlined
          v-tooltip.bottom="'Reise mit ChatGPT planen und als Datei importieren'"
          @click="chatGptDialogVisible = true"
        />
        <Button
          class="hidden md:inline-flex whitespace-nowrap"
          label="Hier bin ich"
          icon="pi pi-map-marker"
          severity="secondary"
          outlined
          v-tooltip.bottom="'Aktuellen Ort als besucht speichern'"
          @click="hereDialogVisible = true"
        />
        <Button class="whitespace-nowrap" label="Neues Ziel" icon="pi pi-plus" @click="openCreateDialog" />
        <Button
          class="hidden md:inline-flex"
          icon="pi pi-refresh"
          severity="secondary"
          text
          rounded
          :loading="loading"
          v-tooltip.bottom="'Daten neu laden'"
          @click="loadPlaces"
        />
        <Button
          v-if="loginConfigured"
          class="hidden md:inline-flex"
          icon="pi pi-user-plus"
          severity="secondary"
          text
          rounded
          v-tooltip.bottom="'Partner einladen'"
          @click="inviteDialogVisible = true"
        />
        <Button
          v-if="loginConfigured"
          class="hidden md:inline-flex"
          icon="pi pi-sign-out"
          severity="secondary"
          text
          rounded
          v-tooltip.bottom="'Abmelden'"
          @click="doLogout"
        />
        <!-- Am Handy: die übrigen Aktionen in einem Menü. -->
        <Button
          class="md:hidden"
          icon="pi pi-ellipsis-v"
          severity="secondary"
          outlined
          aria-label="Weitere Aktionen"
          aria-haspopup="true"
          :loading="importing"
          @click="actionMenu?.toggle($event)"
        />
        <Menu ref="actionMenu" :model="actionMenuItems" popup />
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

    <ChatGptDialog v-model:visible="chatGptDialogVisible" @imported="onTripImported" />

    <InviteDialog v-model:visible="inviteDialogVisible" />

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
      class="rounded-xl overflow-x-auto border border-surface-200 dark:border-surface-700"
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

      <Column field="status" class="w-10">
        <template #body="{ data }">
          <i
            v-if="data.status === 'Visited'"
            class="pi pi-check-circle text-green-600 dark:text-green-400"
            v-tooltip.top="'Besucht'"
          />
          <i v-else class="pi pi-heart text-primary" v-tooltip.top="'Wunschliste'" />
        </template>
      </Column>
      <Column field="name" header="Name" sortable>
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <i :class="data.kind === 'Region' ? 'pi pi-globe' : 'pi pi-map-marker'" class="text-muted-color" />
            <div>
              <div class="font-medium">{{ data.name }}</div>
              <div v-if="data.region" class="text-xs text-muted-color">{{ data.region }}</div>
            </div>
          </div>
        </template>
      </Column>
      <Column field="country" class="w-12">
        <template #body="{ data }">
          <span v-if="countryFlag(data.country)" class="text-xl leading-none" v-tooltip.top="data.country">
            {{ countryFlag(data.country) }}
          </span>
          <span v-else-if="data.country" class="text-sm text-muted-color">{{ data.country }}</span>
          <span v-else class="text-muted-color">–</span>
        </template>
      </Column>
      <Column v-if="nearbyFilterReady" field="distanceKm" header="Entfernung" sortable>
        <template #body="{ data }">
          <span class="font-medium">{{ formatDistance(data.distanceKm) }}</span>
        </template>
      </Column>
      <Column field="overnight" sortable>
        <template #header>
          <i class="pi pi-moon" v-tooltip.top="'Übernachtung'" />
        </template>
        <template #body="{ data }">
          <Tag v-if="data.overnight === 'Stellplatz'" value="SP" severity="info" v-tooltip.top="'Stellplatz'" />
          <Tag v-else-if="data.overnight === 'Campingplatz'" value="CP" severity="success" v-tooltip.top="'Campingplatz'" />
          <Tag v-else-if="data.overnight === 'Frei'" value="Frei" severity="warn" v-tooltip.top="'Frei stehen'" />
          <Tag v-else-if="data.overnight === 'Parkplatz'" value="P" severity="secondary" v-tooltip.top="'Parkplatz'" />
          <span v-else class="text-muted-color">–</span>
        </template>
      </Column>
      <Column field="isStopoverCandidate" sortable>
        <template #header>
          <i class="pi pi-flag" v-tooltip.top="'Zwischenstopp'" />
        </template>
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
