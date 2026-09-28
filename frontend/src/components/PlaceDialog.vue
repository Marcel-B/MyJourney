<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import SelectButton from 'primevue/selectbutton'
import ToggleSwitch from 'primevue/toggleswitch'
import DatePicker from 'primevue/datepicker'
import Rating from 'primevue/rating'
import Button from 'primevue/button'
import type { OvernightType, Place, PlaceInput } from '../types'

const props = defineProps<{
  visible: boolean
  place: Place | null
  saving: boolean
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  save: [input: PlaceInput]
}>()

const kindOptions = [
  { label: 'Ort', value: 'Place' },
  { label: 'Region', value: 'Region' },
]

const statusOptions = [
  { label: 'Wunschliste', value: 'Wishlist' },
  { label: 'Besucht', value: 'Visited' },
]

const overnightOptions = [
  { label: 'Nichts', value: 'None' },
  { label: 'Stellplatz', value: 'Stellplatz' },
  { label: 'Campingplatz', value: 'Campingplatz' },
]

const name = ref('')
const kind = ref<'Place' | 'Region'>('Place')
const status = ref<'Wishlist' | 'Visited'>('Wishlist')
const region = ref('')
const country = ref('')
const latitude = ref<number | null>(null)
const longitude = ref<number | null>(null)
const notes = ref('')
const rating = ref<number | undefined>(undefined)
const visitedAt = ref<Date | null>(null)
const isStopoverCandidate = ref(false)
const overnight = ref<OvernightType>('None')
const submitted = ref(false)

const isEdit = computed(() => props.place !== null)

watch(
  () => props.visible,
  (visible) => {
    if (!visible) return
    submitted.value = false
    const p = props.place
    name.value = p?.name ?? ''
    kind.value = p?.kind ?? 'Place'
    status.value = p?.status ?? 'Wishlist'
    region.value = p?.region ?? ''
    country.value = p?.country ?? ''
    latitude.value = p?.latitude ?? null
    longitude.value = p?.longitude ?? null
    notes.value = p?.notes ?? ''
    rating.value = p?.rating ?? undefined
    visitedAt.value = p?.visitedAt ? new Date(p.visitedAt) : null
    isStopoverCandidate.value = p?.isStopoverCandidate ?? false
    overnight.value = p?.overnight ?? 'None'
  },
)

function toDateOnly(date: Date | null): string | null {
  if (!date) return null
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function submit() {
  submitted.value = true
  if (!name.value.trim()) return
  emit('save', {
    name: name.value.trim(),
    kind: kind.value,
    status: status.value,
    region: region.value.trim() || null,
    country: country.value.trim() || null,
    latitude: latitude.value,
    longitude: longitude.value,
    notes: notes.value.trim() || null,
    rating: status.value === 'Visited' ? rating.value || null : null,
    visitedAt: status.value === 'Visited' ? toDateOnly(visitedAt.value) : null,
    isStopoverCandidate: isStopoverCandidate.value,
    overnight: overnight.value,
  })
}
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="isEdit ? 'Ziel bearbeiten' : 'Neues Ziel'"
    class="w-full max-w-xl mx-4"
    @update:visible="emit('update:visible', $event)"
  >
    <div class="flex flex-col gap-4 pt-1">
      <div class="flex flex-col gap-1">
        <label for="place-name" class="text-sm font-medium">Name *</label>
        <InputText
          id="place-name"
          v-model="name"
          :invalid="submitted && !name.trim()"
          placeholder="z. B. Lofoten, Gardasee, Schwarzwald"
          autofocus
        />
        <small v-if="submitted && !name.trim()" class="text-red-500">Bitte einen Namen angeben.</small>
      </div>

      <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Art</label>
          <SelectButton v-model="kind" :options="kindOptions" option-label="label" option-value="value" :allow-empty="false" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Status</label>
          <SelectButton v-model="status" :options="statusOptions" option-label="label" option-value="value" :allow-empty="false" />
        </div>
      </div>

      <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div class="flex flex-col gap-1">
          <label for="place-region" class="text-sm font-medium">Region</label>
          <InputText id="place-region" v-model="region" placeholder="z. B. Südnorwegen" />
        </div>
        <div class="flex flex-col gap-1">
          <label for="place-country" class="text-sm font-medium">Land</label>
          <InputText id="place-country" v-model="country" placeholder="z. B. Norwegen" />
        </div>
      </div>

      <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div class="flex flex-col gap-1">
          <label for="place-lat" class="text-sm font-medium">Breitengrad</label>
          <InputNumber
            id="place-lat" v-model="latitude" :min-fraction-digits="0" :max-fraction-digits="6"
            :min="-90" :max="90" locale="de-DE" placeholder="optional"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label for="place-lon" class="text-sm font-medium">Längengrad</label>
          <InputNumber
            id="place-lon" v-model="longitude" :min-fraction-digits="0" :max-fraction-digits="6"
            :min="-180" :max="180" locale="de-DE" placeholder="optional"
          />
        </div>
      </div>

      <div v-if="status === 'Visited'" class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div class="flex flex-col gap-1">
          <label for="place-visited" class="text-sm font-medium">Besucht am</label>
          <DatePicker id="place-visited" v-model="visitedAt" date-format="dd.mm.yy" show-icon />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-sm font-medium">Bewertung</label>
          <Rating v-model="rating" class="mt-2" />
        </div>
      </div>

      <div class="flex items-center gap-3">
        <ToggleSwitch v-model="isStopoverCandidate" input-id="place-stopover" />
        <label for="place-stopover" class="text-sm">Eignet sich als Zwischenstopp</label>
      </div>

      <div class="flex flex-col gap-1">
        <label class="text-sm font-medium">Übernachtung vor Ort</label>
        <SelectButton v-model="overnight" :options="overnightOptions" option-label="label" option-value="value" :allow-empty="false" />
      </div>

      <div class="flex flex-col gap-1">
        <label for="place-notes" class="text-sm font-medium">Notizen</label>
        <Textarea id="place-notes" v-model="notes" rows="3" auto-resize placeholder="Was macht dieses Ziel besonders?" />
      </div>
    </div>

    <template #footer>
      <Button label="Abbrechen" severity="secondary" text :disabled="saving" @click="emit('update:visible', false)" />
      <Button :label="isEdit ? 'Speichern' : 'Anlegen'" icon="pi pi-check" :loading="saving" @click="submit" />
    </template>
  </Dialog>
</template>
