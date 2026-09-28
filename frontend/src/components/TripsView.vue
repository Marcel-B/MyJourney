<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { createTrip, deleteTrip, updateTrip } from '../api/places'
import type { Place, Trip, TripInput, TripStopInput } from '../types'
import TripDialog from './TripDialog.vue'

const props = defineProps<{
  trips: Trip[]
  places: Place[]
  loading: boolean
}>()

const emit = defineEmits<{
  changed: []
}>()

const confirm = useConfirm()
const toast = useToast()

const dialogVisible = ref(false)
const editingTrip = ref<Trip | null>(null)
const saving = ref(false)
const expandedId = ref<string | null>(null)

// Stopp hinzufügen: entweder ein erfasster Ort oder ein freier Name.
const addStopTripId = ref<string | null>(null)
const newStopPlaceId = ref<string | null>(null)
const newStopName = ref('')

const placeOptions = computed(() =>
  [...props.places].sort((a, b) => a.name.localeCompare(b.name, 'de')),
)

function formatDate(value: string | null): string | null {
  if (!value) return null
  return new Date(value).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

function dateRange(trip: Trip): string | null {
  const start = formatDate(trip.startDate)
  const end = formatDate(trip.endDate)
  if (start && end) return `${start} – ${end}`
  return start ?? end
}

function toggleExpand(trip: Trip) {
  expandedId.value = expandedId.value === trip.id ? null : trip.id
  addStopTripId.value = null
}

function openCreateDialog() {
  editingTrip.value = null
  dialogVisible.value = true
}

function openEditDialog(trip: Trip) {
  editingTrip.value = trip
  dialogVisible.value = true
}

async function saveTrip(input: TripInput) {
  saving.value = true
  try {
    if (editingTrip.value) {
      await updateTrip(editingTrip.value.id, input)
      toast.add({ severity: 'success', summary: 'Gespeichert', detail: `„${input.name}" wurde aktualisiert.`, life: 3000 })
    } else {
      const created = await createTrip(input)
      expandedId.value = created.id
      toast.add({ severity: 'success', summary: 'Reise angelegt', detail: `„${created.name}" ist startklar – füge Stopps hinzu.`, life: 4000 })
    }
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Speichern fehlgeschlagen', detail: String(error), life: 6000 })
  } finally {
    saving.value = false
  }
}

function confirmDelete(trip: Trip) {
  confirm.require({
    message: `Reise „${trip.name}" mit ${trip.stops.length} Stopps löschen?`,
    header: 'Reise löschen',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Löschen',
    rejectLabel: 'Abbrechen',
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await deleteTrip(trip.id)
        toast.add({ severity: 'success', summary: 'Gelöscht', detail: `„${trip.name}" wurde entfernt.`, life: 3000 })
        emit('changed')
      } catch (error) {
        toast.add({ severity: 'error', summary: 'Löschen fehlgeschlagen', detail: String(error), life: 6000 })
      }
    },
  })
}

function stopsAsInput(trip: Trip): TripStopInput[] {
  return trip.stops.map((s) => ({
    placeId: s.placeId,
    name: s.name,
    latitude: s.latitude,
    longitude: s.longitude,
    notes: s.notes,
  }))
}

async function saveStops(trip: Trip, stops: TripStopInput[], successDetail: string) {
  try {
    await updateTrip(trip.id, {
      name: trip.name,
      notes: trip.notes,
      startDate: trip.startDate,
      endDate: trip.endDate,
      stops,
    })
    toast.add({ severity: 'success', summary: 'Gespeichert', detail: successDetail, life: 2500 })
    emit('changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Speichern fehlgeschlagen', detail: String(error), life: 6000 })
  }
}

function moveStop(trip: Trip, index: number, delta: number) {
  const stops = stopsAsInput(trip)
  const target = index + delta
  if (target < 0 || target >= stops.length) return
  const [stop] = stops.splice(index, 1)
  stops.splice(target, 0, stop)
  saveStops(trip, stops, 'Reihenfolge aktualisiert.')
}

function removeStop(trip: Trip, index: number) {
  const stops = stopsAsInput(trip)
  const [removed] = stops.splice(index, 1)
  saveStops(trip, stops, `Stopp „${removed.name ?? ''}" entfernt.`)
}

function openAddStop(trip: Trip) {
  addStopTripId.value = trip.id
  newStopPlaceId.value = null
  newStopName.value = ''
}

function addStop(trip: Trip) {
  const stops = stopsAsInput(trip)
  if (newStopPlaceId.value) {
    const place = props.places.find((p) => p.id === newStopPlaceId.value)
    stops.push({ placeId: newStopPlaceId.value })
    saveStops(trip, stops, `„${place?.name ?? 'Stopp'}" hinzugefügt.`)
  } else if (newStopName.value.trim()) {
    stops.push({ name: newStopName.value.trim() })
    saveStops(trip, stops, `„${newStopName.value.trim()}" hinzugefügt.`)
  } else {
    return
  }
  addStopTripId.value = null
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between">
      <p class="text-sm text-muted-color m-0">
        {{ trips.length }} {{ trips.length === 1 ? 'Reise' : 'Reisen' }} – Stopps in Fahrreihenfolge. Auf der Karte lässt sich jede Reise als Route anzeigen.
      </p>
      <Button label="Neue Reise" icon="pi pi-plus" @click="openCreateDialog" />
    </div>

    <div v-if="!loading && trips.length === 0" class="text-center py-14 text-muted-color rounded-xl border border-surface-200 dark:border-surface-700">
      <i class="pi pi-compass block text-4xl mb-3" />
      Noch keine Reise geplant. Leg mit „Neue Reise" los – oder lass dir von deinem KI-Assistenten eine Route zusammenstellen.
    </div>

    <div
      v-for="trip in trips"
      :key="trip.id"
      class="rounded-xl border border-surface-200 dark:border-surface-700 overflow-hidden"
    >
      <div
        class="flex flex-wrap items-center gap-3 px-4 py-3 cursor-pointer hover:bg-surface-50 dark:hover:bg-surface-800"
        @click="toggleExpand(trip)"
      >
        <i :class="expandedId === trip.id ? 'pi pi-chevron-down' : 'pi pi-chevron-right'" class="text-muted-color" />
        <div class="flex-1 min-w-48">
          <div class="font-semibold">{{ trip.name }}</div>
          <div class="text-sm text-muted-color">
            {{ trip.stops.length }} {{ trip.stops.length === 1 ? 'Stopp' : 'Stopps' }}<template v-if="dateRange(trip)"> · {{ dateRange(trip) }}</template>
          </div>
        </div>
        <div class="flex items-center gap-1" @click.stop>
          <Button icon="pi pi-pencil" text rounded severity="secondary" aria-label="Bearbeiten" @click="openEditDialog(trip)" />
          <Button icon="pi pi-trash" text rounded severity="danger" aria-label="Löschen" @click="confirmDelete(trip)" />
        </div>
      </div>

      <div v-if="expandedId === trip.id" class="px-4 pb-4 flex flex-col gap-3 border-t border-surface-200 dark:border-surface-700 pt-3">
        <p v-if="trip.notes" class="text-sm text-muted-color whitespace-pre-line m-0">{{ trip.notes }}</p>

        <ol class="m-0 p-0 list-none flex flex-col gap-2">
          <li
            v-for="(stop, index) in trip.stops"
            :key="stop.id"
            class="flex items-start gap-3 rounded-lg bg-surface-50 dark:bg-surface-800 px-3 py-2"
          >
            <Tag :value="String(stop.order)" severity="warn" class="mt-0.5" />
            <div class="flex-1 min-w-0">
              <div class="font-medium">
                {{ stop.name }}
                <i v-if="stop.latitude != null" class="pi pi-map-marker text-xs text-muted-color ml-1" title="Mit Koordinaten – erscheint auf der Karte" />
              </div>
              <div v-if="stop.notes" class="text-sm text-muted-color whitespace-pre-line">{{ stop.notes }}</div>
            </div>
            <div class="flex items-center">
              <Button icon="pi pi-arrow-up" text rounded size="small" severity="secondary" :disabled="index === 0" aria-label="Nach oben" @click="moveStop(trip, index, -1)" />
              <Button icon="pi pi-arrow-down" text rounded size="small" severity="secondary" :disabled="index === trip.stops.length - 1" aria-label="Nach unten" @click="moveStop(trip, index, 1)" />
              <Button icon="pi pi-times" text rounded size="small" severity="danger" aria-label="Stopp entfernen" @click="removeStop(trip, index)" />
            </div>
          </li>
        </ol>

        <div v-if="addStopTripId === trip.id" class="flex flex-wrap items-center gap-2">
          <Select
            v-model="newStopPlaceId"
            :options="placeOptions"
            option-label="name"
            option-value="id"
            placeholder="Erfassten Ort wählen …"
            filter
            show-clear
            class="min-w-64"
          />
          <span class="text-sm text-muted-color">oder</span>
          <InputText v-model="newStopName" placeholder="Freier Stopp (Name)" :disabled="!!newStopPlaceId" @keyup.enter="addStop(trip)" />
          <Button label="Hinzufügen" icon="pi pi-check" size="small" :disabled="!newStopPlaceId && !newStopName.trim()" @click="addStop(trip)" />
          <Button label="Abbrechen" text size="small" severity="secondary" @click="addStopTripId = null" />
        </div>
        <div v-else>
          <Button label="Stopp hinzufügen" icon="pi pi-plus" text size="small" @click="openAddStop(trip)" />
        </div>
      </div>
    </div>

    <TripDialog v-model:visible="dialogVisible" :trip="editingTrip" :saving="saving" @save="saveTrip" />
  </div>
</template>
