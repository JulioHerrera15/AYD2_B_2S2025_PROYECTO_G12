// @ts-check
const { defineConfig, devices } = require('@playwright/test');

/**
 * Configuración de Playwright para SGO-Logic Frontend Web.
 * Conexión predeterminada a http://localhost:8092 (o variable WEB_BASE_URL).
 */
module.exports = defineConfig({
  testDir: './specs',
  timeout: 45000,
  expect: {
    timeout: 10000
  },
  fullyParallel: false, // Ejecutar en serie para evitar conflictos de carrito/stock compartido en pruebas E2E
  retries: 1,
  workers: 1,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }]
  ],
  use: {
    baseURL: process.env.WEB_BASE_URL || 'http://localhost:8092',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    headless: true,
    viewport: { width: 1280, height: 800 }
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    }
  ],
});
