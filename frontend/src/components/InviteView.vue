<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import { isAxiosError } from 'axios'

import { acceptInvite, fetchInviteStatus } from '../api/places'

const props = defineProps<{ token: string }>()
const emit = defineEmits<{ accepted: [] }>()

const checking = ref(true)
const inviteError = ref('')
const username = ref('')
const password = ref('')
const passwordRepeat = ref('')
const formError = ref('')
const submitting = ref(false)

onMounted(async () => {
  try {
    const status = await fetchInviteStatus(props.token)
    if (!status.valid) inviteError.value = status.error || 'Diese Einladung ist nicht gültig.'
  } catch {
    inviteError.value = 'Die Einladung konnte nicht geprüft werden. Ist der Server erreichbar?'
  } finally {
    checking.value = false
  }
})

async function submit() {
  if (!username.value.trim() || !password.value) return
  if (password.value !== passwordRepeat.value) {
    formError.value = 'Die Passwörter stimmen nicht überein.'
    return
  }
  submitting.value = true
  formError.value = ''
  try {
    await acceptInvite(props.token, username.value.trim(), password.value)
    emit('accepted')
  } catch (e) {
    if (isAxiosError(e) && e.response?.status === 429) {
      formError.value = 'Zu viele Versuche – bitte eine Minute warten.'
    } else if (isAxiosError(e) && e.response?.data?.error) {
      formError.value = String(e.response.data.error)
    } else {
      formError.value = 'Die Registrierung ist fehlgeschlagen. Bitte noch einmal versuchen.'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="min-h-screen grid place-items-center px-4">
    <div class="w-full max-w-sm rounded-xl border border-surface-200 dark:border-surface-700 bg-surface-0 dark:bg-surface-900 p-8 flex flex-col gap-4">
      <div class="text-center">
        <i class="pi pi-compass text-primary" style="font-size: 2.5rem" />
        <h1 class="text-2xl font-bold mt-2 mb-1">MyJourney</h1>
        <p class="text-sm text-muted-color m-0">Du wurdest eingeladen, gemeinsam Reisen zu planen.</p>
      </div>

      <div v-if="checking" class="text-center text-muted-color text-sm py-4">
        <i class="pi pi-spinner pi-spin mr-2" />Einladung wird geprüft …
      </div>

      <div v-else-if="inviteError" class="flex flex-col gap-3 text-center">
        <p class="text-sm text-red-600 dark:text-red-400 m-0">{{ inviteError }}</p>
        <p class="text-sm text-muted-color m-0">Bitte um einen neuen Einladelink, der Link ist 7 Tage gültig und nur einmal nutzbar.</p>
      </div>

      <form v-else class="flex flex-col gap-4" @submit.prevent="submit">
        <div class="flex flex-col gap-1">
          <label for="invite-username" class="text-sm">Dein Benutzername</label>
          <InputText id="invite-username" v-model="username" autocomplete="username" autofocus class="w-full" />
        </div>
        <div class="flex flex-col gap-1">
          <label for="invite-password" class="text-sm">Passwort (mindestens 8 Zeichen)</label>
          <Password
            v-model="password"
            input-id="invite-password"
            :feedback="false"
            toggle-mask
            input-class="w-full"
            class="w-full"
            :input-props="{ autocomplete: 'new-password' }"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label for="invite-password-repeat" class="text-sm">Passwort wiederholen</label>
          <Password
            v-model="passwordRepeat"
            input-id="invite-password-repeat"
            :feedback="false"
            toggle-mask
            input-class="w-full"
            class="w-full"
            :input-props="{ autocomplete: 'new-password' }"
          />
        </div>
        <p v-if="formError" class="text-sm text-red-600 dark:text-red-400 m-0">{{ formError }}</p>
        <Button
          type="submit"
          label="Zugang anlegen"
          icon="pi pi-user-plus"
          :loading="submitting"
          :disabled="!username.trim() || !password || !passwordRepeat"
        />
      </form>
    </div>
  </div>
</template>
