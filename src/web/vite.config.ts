import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, import.meta.dirname, 'API_URL')
  return {
    plugins: [react()],
    define: { 'import.meta.env.API_URL': JSON.stringify(env.API_URL || 'http://localhost:7071') },
    server: { host: '127.0.0.1', port: 5173 },
  }
})
