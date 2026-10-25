import { defineConfig, devices } from '@playwright/test'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  testDir: '.',
  fullyParallel: true,
  workers: 2,
  use: {
    baseURL: 'http://127.0.0.1:5179',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 5179 --strictPort',
    cwd: fileURLToPath(new URL('../../src/web', import.meta.url)),
    env: { API_URL: 'https://review.example.test/api/', FUNCTION_KEY: 'test-only-not-a-real-key' },
    url: 'http://127.0.0.1:5179',
    reuseExistingServer: false,
  },
})