import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, import.meta.dirname, ['API_URL', 'FUNCTION_KEY'])
  return {
    plugins: [react()],
    define: {
      'import.meta.env.API_URL': JSON.stringify(env.API_URL || 'http://localhost:7071'),
      'import.meta.env.FUNCTION_KEY': JSON.stringify(env.FUNCTION_KEY || ''),
    },
    server: { host: 'localhost', port: 5173 },
  }
})
