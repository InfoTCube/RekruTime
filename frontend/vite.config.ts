import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Backend from backend/RekruTime.Api (launchSettings.json)
      '/api': 'http://localhost:5080',
    },
  },
})
