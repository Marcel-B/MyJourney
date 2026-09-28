<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import DatePicker from 'primevue/datepicker'
import Button from 'primevue/button'
import type { Trip, TripInput } from '../types'

const props = defineProps<{
  visible: boolean
  trip: Trip | null
  saving: boolean
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  save: [input: TripInput]
}>()

const name = ref('')
const notes = ref('')
const startDate = ref<Date | null>(null)
const endDate = ref<Date | null>(null)

watch(
  () => props.visible,
  (visible) => {
    if (!visible) return
    name.value = props.trip?.name ?? ''
    notes.value = props.trip?.notes ?? ''
    startDate.value = props.trip?.startDate ? new Date(props.trip.startDate) : null
    endDate.value = props.trip?.endDate ? new Date(props.trip.endDate) : null
  },
)

const valid = computed(() => name.value.trim().length > 0)

// Als lokales Datum (YYYY-MM-DD) serialisieren, ohne Zeitzonen-Verschiebung.
function toDateString(date: Date | null): string | null {
  if (!date) return null
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${m}-${d}`
}

function submit() {
  if (!valid.value) return
  emit('save', {
    name: name.value.trim(),
    notes: notes.value.trim() || null,
    startDate: toDateString(startDate.value),
    endDate: toDateString(endDate.value),
    // Stopps übernimmt die Reisen-Ansicht – hier nur die Stammdaten.
    stops: props.trip?.stops.map((s) => ({
      placeId: s.placeId,
      name: s.name,
      latitude: s.latitude,
      longitude: s.longitude,
      notes: s.notes,
    })),
  })
}
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="trip ? 'Reise bearbeiten' : 'Neue Reise'"
    class="w-full max-w-lg mx-4"
    @update:visible="emit('update:visible', $event)"
  >
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-1">
        <label for="trip-name" class="text-sm font-medium">Name *</label>
        <InputText id="trip-name" v-model="name" autofocus placeholder="z. B. Norwegen im Sommer" @keyup.enter="submit" />
      </div>
      <div class="grid grid-cols-2 gap-3">
        <div class="flex flex-col gap-1">
          <label for="trip-start" class="text-sm font-medium">Start</label>
          <DatePicker id="trip-start" v-model="startDate" date-format="dd.mm.yy" show-icon show-button-bar />
        </div>
        <div class="flex flex-col gap-1">
          <label for="trip-end" class="text-sm font-medium">Ende</label>
          <DatePicker id="trip-end" v-model="endDate" date-format="dd.mm.yy" show-icon show-button-bar :min-date="startDate ?? undefined" />
        </div>
      </div>
      <div class="flex flex-col gap-1">
        <label for="trip-notes" class="text-sm font-medium">Notizen</label>
        <Textarea id="trip-notes" v-model="notes" rows="4" auto-resize placeholder="Fähren, Maut, grobe Planung …" />
      </div>
    </div>
    <template #footer>
      <Button label="Abbrechen" severity="secondary" text :disabled="saving" @click="emit('update:visible', false)" />
      <Button :label="trip ? 'Speichern' : 'Anlegen'" icon="pi pi-check" :loading="saving" :disabled="!valid" @click="submit" />
    </template>
  </Dialog>
</template>
