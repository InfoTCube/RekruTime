import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  base: './', // relative asset URLs, resolved against <base href> – works under any sub-path
  server: {
    proxy: {
      // Backend from backend/RekruTime.Api (launchSettings.json)
      '/api': 'http://localhost:5080',
    },
  },
})
