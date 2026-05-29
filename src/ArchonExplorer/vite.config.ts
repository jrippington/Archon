import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';

/**
 * Creates the Vite configuration for the ArchonExplorer browser application.
 *
 * React support remains the only runtime plugin, while the local `@` alias matches the
 * shadcn/ui component metadata so shared primitives can import helpers without brittle
 * relative paths.
 *
 * @returns The Vite configuration consumed by development hosting and production builds.
 */
function createViteConfiguration() {
  // React remains the only Vite plugin for this slice; the alias below is a compile-time
  // convenience that keeps shell components and UI primitives aligned with components.json. The
  // Vitest exclusion keeps Playwright browser journeys in their own runner even when contributors
  // pass broad filters such as `workbench` to the unit-test command.
  return defineConfig({
    plugins: [react()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    test: {
      exclude: ['src/test-e2e/**'],
    },
  });
}

export default createViteConfiguration();
