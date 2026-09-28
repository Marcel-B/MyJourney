import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  server: {
    proxy: {
      // Backend im Dev-Modus: dotnet run im Ordner backend/MyJourney.Api (Port 5032)
      '/api': { target: 'http://localhost:5032', changeOrigin: true },
      '/mcp': { target: 'http://localhost:5032', changeOrigin: true },
    },
  },
})
