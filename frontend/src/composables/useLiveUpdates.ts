import { onBeforeUnmount } from 'vue'
import { apiBaseUrl, clientId, getApiKey } from '../api/places'

interface DataChangedEvent {
  version: number
  origin: string | null
}

/**
 * Hält eine SSE-Verbindung zu /api/events und ruft onChange auf, sobald jemand anderes
 * (Partner, ChatGPT/Claude über MCP, Import) die Daten geändert hat. Für die PWA wichtig:
 * Beim Zurückkehren in die App wird die Verbindung sofort neu aufgebaut und – falls die
 * App länger im Hintergrund war – sicherheitshalber neu geladen, auch wenn der Stream
 * unterwegs (z. B. durch den Mobil-Browser) gekappt wurde.
 */
export function useLiveUpdates(onChange: () => void) {
  let controller: AbortController | null = null
  let started = false
  let lastVersion: number | null = null
  let retryDelay = 1000
  let retryTimer: number | undefined
  let reloadTimer: number | undefined
  let hiddenAt: number | null = null

  // Mehrere Ereignisse kurz hintereinander (z. B. ein Import mit vielen Orten)
  // lösen nur ein Neuladen aus.
  function scheduleReload() {
    window.clearTimeout(reloadTimer)
    reloadTimer = window.setTimeout(() => onChange(), 300)
  }

  function handleEvent(evt: DataChangedEvent) {
    // Das erste Ereignis einer Verbindung trägt nur den aktuellen Stand; nach einem
    // Reconnect verrät eine abweichende Version, dass zwischenzeitlich etwas passiert ist.
    if (lastVersion !== null && evt.version !== lastVersion && evt.origin !== clientId) {
      scheduleReload()
    }
    lastVersion = evt.version
  }

  function scheduleRetry() {
    window.clearTimeout(retryTimer)
    retryTimer = window.setTimeout(() => connect(), retryDelay)
    retryDelay = Math.min(retryDelay * 2, 30000)
  }

  async function connect() {
    if (!started) return
    controller?.abort()
    controller = new AbortController()
    try {
      const headers: Record<string, string> = {
        Accept: 'text/event-stream',
        'X-Client-Id': clientId,
      }
      const key = getApiKey()
      if (key) headers['X-Api-Key'] = key

      const response = await fetch(`${apiBaseUrl}/api/events`, {
        headers,
        signal: controller.signal,
        cache: 'no-store',
      })
      if (response.status === 401) {
        // Nicht (mehr) angemeldet: kein Dauer-Retry; nach dem nächsten
        // erfolgreichen Laden wird start() erneut aufgerufen.
        started = false
        return
      }
      if (!response.ok || !response.body) throw new Error(`HTTP ${response.status}`)
      retryDelay = 1000

      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      for (;;) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        let index: number
        while ((index = buffer.indexOf('\n\n')) >= 0) {
          const chunk = buffer.slice(0, index)
          buffer = buffer.slice(index + 2)
          const data = chunk
            .split('\n')
            .filter((line) => line.startsWith('data:'))
            .map((line) => line.slice(5).trim())
            .join('')
          if (!data) continue
          try {
            handleEvent(JSON.parse(data) as DataChangedEvent)
          } catch {
            // Unlesbare Zeile ignorieren – der nächste Event kommt bestimmt.
          }
        }
      }
      // Server oder Proxy hat den Stream beendet: neu verbinden.
      throw new Error('Stream beendet')
    } catch {
      if (controller?.signal.aborted || !started) return
      scheduleRetry()
    }
  }

  function start() {
    if (started) return
    started = true
    retryDelay = 1000
    connect()
  }

  function stop() {
    started = false
    window.clearTimeout(retryTimer)
    window.clearTimeout(reloadTimer)
    controller?.abort()
    controller = null
    lastVersion = null
  }

  // Zurück in der App (PWA aus dem Hintergrund, Tab-Wechsel, Netz wieder da):
  // Verbindung sofort neu aufbauen; war die App länger als eine Minute weg,
  // zusätzlich neu laden – als Fallback, falls der Stream unbemerkt tot war.
  function onBackInApp() {
    if (!started) return
    const awayMs = hiddenAt ? Date.now() - hiddenAt : 0
    hiddenAt = null
    window.clearTimeout(retryTimer)
    retryDelay = 1000
    connect()
    if (awayMs > 60000) scheduleReload()
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'hidden') {
      hiddenAt = Date.now()
    } else {
      onBackInApp()
    }
  }

  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('online', onBackInApp)

  onBeforeUnmount(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    window.removeEventListener('online', onBackInApp)
    stop()
  })

  return { start, stop }
}
