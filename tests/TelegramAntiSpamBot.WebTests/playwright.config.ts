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
    env: {
      API_URL: 'https://review.example.test/api/',
      TENANT_ID: '11111111-1111-1111-1111-111111111111',
      CLIENT_ID: '22222222-2222-2222-2222-222222222222',
      API_SCOPE: 'api://22222222-2222-2222-2222-222222222222/Review.Access',
      REDIRECT_URI: 'http://127.0.0.1:5179',
    },
    url: 'http://127.0.0.1:5179',
    reuseExistingServer: false,
  },
})