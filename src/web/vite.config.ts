import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, import.meta.dirname, ['API_URL', 'TENANT_ID', 'CLIENT_ID', 'API_SCOPE', 'REDIRECT_URI'])
  return {
    plugins: [react()],
    define: {
      'import.meta.env.API_URL': JSON.stringify(env.API_URL || 'http://localhost:7071'),
      'import.meta.env.TENANT_ID': JSON.stringify(env.TENANT_ID || ''),
      'import.meta.env.CLIENT_ID': JSON.stringify(env.CLIENT_ID || ''),
      'import.meta.env.API_SCOPE': JSON.stringify(env.API_SCOPE || ''),
      'import.meta.env.REDIRECT_URI': JSON.stringify(env.REDIRECT_URI || ''),
    },
    server: { host: 'localhost', port: 5173, strictPort: true },
  }
})
