<script setup lang="ts">
import { ref, watch } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import { useToast } from 'primevue/usetoast'
import { isAxiosError } from 'axios'

import { createInvite } from '../api/places'

const visible = defineModel<boolean>('visible', { required: true })

const toast = useToast()
const creating = ref(false)
const inviteUrl = ref('')
const expiresAt = ref('')
const error = ref('')

// Beim Öffnen direkt einen frischen Link erzeugen – ein Klick weniger.
watch(visible, (open) => {
  if (open) {
    inviteUrl.value = ''
    error.value = ''
    void generate()
  }
})

async function generate() {
  creating.value = true
  error.value = ''
  try {
    const invite = await createInvite()
    inviteUrl.value = `${window.location.origin}/invite?token=${invite.token}`
    expiresAt.value = new Date(invite.expiresAt).toLocaleDateString('de-DE', {
      day: '2-digit', month: '2-digit', year: 'numeric',
    })
  } catch (e) {
    error.value = isAxiosError(e) && e.response?.data?.error
      ? String(e.response.data.error)
      : 'Der Einladelink konnte nicht erzeugt werden.'
  } finally {
    creating.value = false
  }
}

async function copyLink() {
  try {
    await navigator.clipboard.writeText(inviteUrl.value)
    toast.add({ severity: 'success', summary: 'Kopiert', detail: 'Der Einladelink ist in der Zwischenablage.', life: 3000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Kopieren fehlgeschlagen', detail: 'Bitte den Link von Hand markieren und kopieren.', life: 5000 })
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Partner einladen" class="w-full max-w-lg mx-4">
    <p class="mt-0 text-sm text-muted-color">
      Mit diesem Link legt sich dein Partner einen eigenen Zugang an – mit eigenem
      Benutzernamen und Passwort, auf denselben Reisedaten. Der Link ist 7 Tage gültig
      und funktioniert genau einmal.
    </p>

    <div v-if="error" class="text-sm text-red-600 dark:text-red-400">{{ error }}</div>

    <div v-else class="flex flex-col gap-2">
      <div class="flex gap-2">
        <InputText :model-value="inviteUrl" readonly class="w-full text-sm" :placeholder="creating ? 'Link wird erzeugt …' : ''" @focus="($event.target as HTMLInputElement).select()" />
        <Button icon="pi pi-copy" label="Kopieren" :disabled="!inviteUrl" @click="copyLink" />
      </div>
      <p v-if="expiresAt" class="text-xs text-muted-color m-0">Gültig bis {{ expiresAt }}.</p>
    </div>

    <template #footer>
      <Button label="Neuen Link erzeugen" icon="pi pi-refresh" severity="secondary" text :loading="creating" @click="generate" />
      <Button label="Fertig" icon="pi pi-check" @click="visible = false" />
    </template>
  </Dialog>
</template>
