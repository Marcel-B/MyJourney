<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import DataTable from 'primevue/datatable'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import InputText from 'primevue/inputtext'
import Rating from 'primevue/rating'
import SelectButton from 'primevue/selectbutton'
import Tag from 'primevue/tag'
import Toast from 'primevue/toast'
import ToggleSwitch from 'primevue/toggleswitch'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { isAxiosError } from 'axios'

import PlaceDialog from './components/PlaceDialog.vue'
import { createPlace, deletePlace, fetchPlaces, markVisited, updatePlace } from './api/places'
import type { Place, PlaceInput, PlaceStatus } from './types'

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

const statusFilterOptions = [
  { label: 'Alle', value: 'All' },
  { label: 'Wunschliste', value: 'Wishlist' },
  { label: 'Besucht', value: 'Visited' },
]

const wishlistCount = computed(() => places.value.filter((p) => p.status === 'Wishlist').length)
const visitedCount = computed(() => places.value.filter((p) => p.status === 'Visited').length)
const stopoverCount = computed(() => places.value.filter((p) => p.isStopoverCandidate).length)

const filteredPlaces = computed(() => {
  const term = search.value.trim().toLowerCase()
  return places.value.filter((p) => {
    if (statusFilter.value !== 'All' && p.status !== statusFilter.value) return false
    if (stopoversOnly.value && !p.isStopoverCandidate) return false
    if (!term) return true
    return [p.name, p.region, p.country, p.notes]
      .some((field) => field?.toLowerCase().includes(term))
  })
})

function errorMessage(error: unknown): string {
  if (isAxiosError(error)) {
    if (error.response?.status === 401) return 'API-Key fehlt oder ist ungültig.'
    if (error.response) return `Der Server hat mit Status ${error.response.status} geantwortet.`
    return 'Das Backend ist nicht erreichbar. Läuft dotnet run?'
  }
  return 'Unerwarteter Fehler.'
}

async function loadPlaces() {
  loading.value = true
  try {
    places.value = await fetchPlaces()
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Laden fehlgeschlagen', detail: errorMessage(error), life: 6000 })
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
    toast.add({ severity: 'error', summary: 'Speichern fehlgeschlagen', detail: errorMessage(error), life: 6000 })
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
      <Button label="Neues Ziel" icon="pi pi-plus" @click="openCreateDialog" />
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
      <SelectButton v-model="statusFilter" :options="statusFilterOptions" option-label="label" option-value="value" :allow-empty="false" />
      <IconField class="flex-1 min-w-52">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" placeholder="Suchen nach Name, Region, Land …" class="w-full" />
      </IconField>
      <div class="flex items-center gap-2">
        <ToggleSwitch v-model="stopoversOnly" input-id="filter-stopover" />
        <label for="filter-stopover" class="text-sm">Nur Zwischenstopps</label>
      </div>
    </section>

    <DataTable
      :value="filteredPlaces"
      :loading="loading"
      data-key="id"
      paginator
      :rows="10"
      :rows-per-page-options="[10, 25, 50]"
      sort-field="name"
      :sort-order="1"
      class="rounded-xl overflow-hidden border border-surface-200 dark:border-surface-700"
    >
      <template #empty>
        <div class="text-center py-10 text-muted-color">
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
</template>
