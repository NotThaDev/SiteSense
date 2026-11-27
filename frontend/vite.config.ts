import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Default: the API from `dotnet run`/`dotnet watch` (launchSettings); API_URL=http://localhost:8080 for the Docker API.
// changeOrigin false keeps the Host header, so the OIDC redirect URI is :5173.
const api = { target: process.env.API_URL ?? 'http://localhost:5073', changeOrigin: false }

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'src') } },
  // ws: only on /api (the SignalR hub).
  server: { proxy: { '/api': { ...api, ws: true }, '/signin-oidc': api, '/signout-callback-oidc': api } },
})
