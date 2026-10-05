import { ref, onBeforeUnmount } from 'vue'
import { registerSW } from 'virtual:pwa-register'

/**
 * Meldet, wenn auf dem Server eine neue Version der App liegt. Der Browser prüft das
 * von sich aus nur beim App-Start (und höchstens alle 24 h); hier wird zusätzlich beim
 * Zurückkehren in die App und stündlich nachgeschaut. Die neue Version wird erst mit
 * applyUpdate() aktiviert (lädt die Seite neu), damit kein Reload mitten in einer
 * Eingabe passiert.
 */
export function useAppUpdate() {
  const updateAvailable = ref(false)

  let registration: ServiceWorkerRegistration | undefined
  let checkTimer: number | undefined
  let lastCheck = 0

  const updateServiceWorker = registerSW({
    onNeedRefresh() {
      updateAvailable.value = true
    },
    onRegisteredSW(_swUrl, reg) {
      registration = reg
      checkTimer = window.setInterval(checkForUpdate, 60 * 60 * 1000)
    },
  })

  function checkForUpdate() {
    // Kurz hintereinander (Tab-Wechsel hin und her) reicht eine Prüfung.
    const now = Date.now()
    if (now - lastCheck < 60000) return
    lastCheck = now
    registration?.update().catch(() => {
      // Offline oder Server nicht erreichbar – beim nächsten Mal wieder.
    })
  }

  function applyUpdate() {
    void updateServiceWorker(true)
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible') checkForUpdate()
  }

  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('online', checkForUpdate)

  onBeforeUnmount(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    window.removeEventListener('online', checkForUpdate)
    window.clearInterval(checkTimer)
  })

  return { updateAvailable, applyUpdate }
}
