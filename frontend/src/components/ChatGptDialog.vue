<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import { isAxiosError } from 'axios'
import { fetchChatGptPrompt, fetchExchangeExport, importTripFile } from '../api/places'
import type { TripImportResult } from '../types'

const visible = defineModel<boolean>('visible', { required: true })

const emit = defineEmits<{
  imported: [result: TripImportResult]
}>()

const toast = useToast()

const exporting = ref(false)
const copying = ref(false)
const importing = ref(false)
const importErrors = ref<string[]>([])
const fileInput = ref<HTMLInputElement | null>(null)

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}

async function downloadExport() {
  exporting.value = true
  try {
    downloadBlob(await fetchExchangeExport(), 'myjourney-bestand.json')
  } catch {
    toast.add({ severity: 'error', summary: 'Export fehlgeschlagen', detail: 'Der Bestand konnte nicht geladen werden.', life: 6000 })
  } finally {
    exporting.value = false
  }
}

async function copyPrompt() {
  copying.value = true
  try {
    const prompt = await fetchChatGptPrompt()
    try {
      await navigator.clipboard.writeText(prompt)
      toast.add({ severity: 'success', summary: 'Prompt kopiert', detail: 'Der Text liegt in der Zwischenablage – einfach bei ChatGPT einfügen.', life: 4000 })
    } catch {
      // Zwischenablage nicht verfügbar (z. B. ohne HTTPS) – dann als Datei anbieten.
      downloadBlob(new Blob([prompt], { type: 'text/plain' }), 'myjourney-chatgpt-prompt.txt')
    }
  } catch {
    toast.add({ severity: 'error', summary: 'Laden fehlgeschlagen', detail: 'Der Prompt-Text konnte nicht geladen werden.', life: 6000 })
  } finally {
    copying.value = false
  }
}

async function onFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  importErrors.value = []
  importing.value = true
  try {
    let parsed: unknown
    try {
      parsed = JSON.parse(await file.text())
    } catch {
      importErrors.value = ['Die Datei ist kein gültiges JSON. Bitte ChatGPT um die Reise als JSON-Datei nach dem Schema bitten.']
      return
    }

    const result = await importTripFile(parsed)
    const parts = [
      `${result.trip.stops.length} Stopps`,
      result.createdPlaces.length ? `${result.createdPlaces.length} neue Wunschziele` : null,
      result.linkedPlaces.length ? `${result.linkedPlaces.length} vorhandene Orte verknüpft` : null,
    ].filter(Boolean)
    toast.add({
      severity: 'success',
      summary: `Reise „${result.trip.name}" importiert`,
      detail: parts.join(', ') + '.',
      life: 6000,
    })
    visible.value = false
    emit('imported', result)
  } catch (error) {
    if (isAxiosError(error) && error.response?.status === 400) {
      const errors = error.response.data?.errors as Record<string, string[]> | undefined
      importErrors.value = errors
        ? Object.values(errors).flat()
        : ['Die Datei entspricht nicht dem Schema "myjourney-trip" (Version 1). Bitte ChatGPT um eine Datei genau nach dem Schema aus dem Prompt bitten.']
    } else {
      importErrors.value = ['Der Import ist fehlgeschlagen. Ist das Backend erreichbar?']
    }
  } finally {
    importing.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Reise mit ChatGPT planen" class="w-full max-w-lg mx-4">
    <div class="flex flex-col gap-4">
      <p class="m-0 text-sm text-muted-color">
        ChatGPT (oder ein anderer KI-Assistent) plant die Reise und liefert sie als JSON-Datei,
        die MyJourney hier importiert. Stopps werden mit vorhandenen Orten verknüpft, neue Ziele
        landen automatisch auf der Wunschliste.
      </p>

      <div class="flex flex-col gap-3">
        <div class="flex items-start gap-3">
          <span class="rounded-full bg-primary-100 dark:bg-primary-900 w-7 h-7 grid place-items-center text-sm font-semibold shrink-0">1</span>
          <div class="flex-1">
            <div class="font-medium">Bestand herunterladen</div>
            <p class="m-0 mt-0.5 text-sm text-muted-color">Deine Orte und Reisen als Datei – gib sie ChatGPT als Anhang mit, damit es deine Wunschziele kennt.</p>
            <Button label="myjourney-bestand.json" icon="pi pi-download" size="small" severity="secondary" outlined class="mt-2" :loading="exporting" @click="downloadExport" />
          </div>
        </div>

        <div class="flex items-start gap-3">
          <span class="rounded-full bg-primary-100 dark:bg-primary-900 w-7 h-7 grid place-items-center text-sm font-semibold shrink-0">2</span>
          <div class="flex-1">
            <div class="font-medium">Prompt kopieren und bei ChatGPT einfügen</div>
            <p class="m-0 mt-0.5 text-sm text-muted-color">Der Text erklärt ChatGPT das Dateiformat. Danach einfach drauflos planen und am Ende nach der JSON-Datei fragen.</p>
            <Button label="Prompt kopieren" icon="pi pi-copy" size="small" severity="secondary" outlined class="mt-2" :loading="copying" @click="copyPrompt" />
          </div>
        </div>

        <div class="flex items-start gap-3">
          <span class="rounded-full bg-primary-100 dark:bg-primary-900 w-7 h-7 grid place-items-center text-sm font-semibold shrink-0">3</span>
          <div class="flex-1">
            <div class="font-medium">Fertige Reise importieren</div>
            <p class="m-0 mt-0.5 text-sm text-muted-color">Die von ChatGPT erzeugte JSON-Datei hier hochladen.</p>
            <Button label="JSON-Datei importieren" icon="pi pi-upload" size="small" class="mt-2" :loading="importing" @click="fileInput?.click()" />
            <input ref="fileInput" type="file" accept=".json,application/json" class="hidden" @change="onFileSelected" />
          </div>
        </div>
      </div>

      <Message v-if="importErrors.length" severity="error" :closable="false">
        <ul class="m-0 pl-4">
          <li v-for="(error, index) in importErrors" :key="index">{{ error }}</li>
        </ul>
      </Message>
    </div>
  </Dialog>
</template>
