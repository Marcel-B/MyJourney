import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
    // PWA: macht die App auf dem Handy "installierbar" (Zum Home-Bildschirm).
    // Der Service Worker cacht nur die App-Shell (JS/CSS/Icons), keine API-Daten –
    // die Daten liegen weiterhin auf dem Server.
    VitePWA({
      // 'prompt': eine neue Version wartet, bis der Nutzer im Hinweis "Aktualisieren"
      // tippt (useAppUpdate + Toast in App.vue), statt die laufende App zu überfahren.
      registerType: 'prompt',
      includeAssets: ['favicon.svg', 'apple-touch-icon.png'],
      manifest: {
        name: 'MyJourney – Wohnmobil-Reiseplaner',
        short_name: 'MyJourney',
        description: 'Wohnmobil-Ziele sammeln, Besuchtes festhalten, Reisen planen.',
        lang: 'de',
        start_url: '/',
        display: 'standalone',
        background_color: '#047857',
        theme_color: '#047857',
        icons: [
          { src: '/pwa-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/pwa-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/pwa-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // Nur statische Dateien vorab cachen; /api und /mcp gehen immer ans Netz.
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        navigateFallbackDenylist: [/^\/api\//, /^\/mcp/, /^\/scalar/, /^\/openapi/],
      },
    }),
  ],
  server: {
    proxy: {
      // Backend im Dev-Modus: dotnet run im Ordner backend/MyJourney.Api (Port 5032)
      '/api': { target: 'http://localhost:5032', changeOrigin: true },
      '/mcp': { target: 'http://localhost:5032', changeOrigin: true },
    },
  },
})
