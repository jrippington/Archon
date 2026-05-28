import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';

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
  // convenience that keeps shell components and UI primitives aligned with components.json.
  return defineConfig({
    plugins: [react()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
  });
}

export default createViteConfiguration();
