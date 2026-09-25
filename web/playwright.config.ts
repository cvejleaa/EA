import { defineConfig, devices } from '@playwright/test';

/**
 * E2E: den rigtige app (Angular + API + PostgreSQL) i en browser, som brugerne ser den. API'et kører i Development
 * mod sin egen database med de fiktive eksempeldata (e2e/start-api.sh), og web på egne porte, så en kørende lokal
 * udviklingsserver ikke forstyrres. Testene deler databasen og kører derfor efter hinanden.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env['CI'],
  // "Flaky" er ikke en årsag: en fejl er en fejl.
  retries: 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:4280',
    locale: 'da-DK',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'bash e2e/start-api.sh',
      url: 'http://localhost:5180/healthz',
      timeout: 240_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: 'npx ng serve --port 4280 --proxy-config proxy.e2e.json',
      url: 'http://localhost:4280',
      timeout: 240_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
