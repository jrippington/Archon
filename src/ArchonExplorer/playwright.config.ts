import { defineConfig, devices } from '@playwright/test';

/**
 * Creates the Playwright configuration used by focused ArchonExplorer browser validation.
 *
 * The configuration stays beside the frontend project so browser-test settings and generated
 * artifacts do not clutter the repository root. It starts the Vite development server for the
 * browser journey and reuses an already running server when a contributor has one open locally.
 *
 * @returns The Playwright configuration for local and CI-style shell validation.
 */
function createPlaywrightConfiguration() {
  // The output folders are intentionally frontend-local and ignored by Playwright defaults so
  // screenshots, traces, and reports remain attached to the project that produced them.
  return defineConfig({
    testDir: './src/test-e2e',
    fullyParallel: false,
    forbidOnly: Boolean(process.env.CI),
    retries: process.env.CI ? 1 : 0,
    reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
    use: {
      baseURL: 'http://127.0.0.1:4173',
      trace: 'on-first-retry',
    },
    webServer: {
      command: 'npm run dev -- --host 127.0.0.1 --port 4173',
      env: {
        // Browser journeys that exercise API-backed surfaces need the request foundation to be
        // configured. Route mocks still provide the responses, so no live ArchonApi is required.
        VITE_ARCHON_API_BASE_URL: 'http://127.0.0.1:4173',
      },
      url: 'http://127.0.0.1:4173',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    projects: [
      {
        name: 'chromium',
        use: { ...devices['Desktop Chrome'] },
      },
    ],
  });
}

export default createPlaywrightConfiguration();
