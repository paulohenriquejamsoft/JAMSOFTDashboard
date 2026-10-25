import { defineConfig, devices } from '@playwright/test';

/**
 * e2e contra API + Web reais e um banco descartável (ver e2e/global-setup.ts). Um worker: os cenários são
 * sequenciais e compartilham o mesmo banco. Chromium roda tudo; Firefox e WebKit rodam o smoke do painel (PWA-06).
 */
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
  },
  outputDir: './e2e/.resultados',
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] }, testMatch: /painel\.spec\.ts/, grep: /@smoke/ },
    { name: 'webkit', use: { ...devices['Desktop Safari'] }, testMatch: /painel\.spec\.ts/, grep: /@smoke/ },
  ],
});
