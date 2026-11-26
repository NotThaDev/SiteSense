import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// changeOrigin false keeps the Host header, so the OIDC redirect URI is :5173.
const api = { target: 'http://localhost:8080', changeOrigin: false }

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'src') } },
  server: { proxy: { '/api': api, '/signin-oidc': api, '/signout-callback-oidc': api } },
})
