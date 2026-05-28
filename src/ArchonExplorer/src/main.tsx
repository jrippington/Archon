import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { ApplicationProviders } from './providers/ApplicationProviders';
import './index.css';

/**
 * Finds the HTML element that receives the React application tree.
 *
 * The explicit guard turns a malformed host page into a clear startup error during local
 * development instead of allowing React to fail later with a less actionable message.
 *
 * @returns The root HTML element declared by the Vite entry document.
 */
function getRequiredRootElement(): HTMLElement {
  // Vite serves index.html with a root element; this null check protects future edits to
  // the host document from silently breaking the bootstrap sequence.
  const rootElement = document.getElementById('root');

  if (rootElement === null) {
    throw new Error('ArchonExplorer could not start because the root element was not found.');
  }

  return rootElement;
}

/**
 * Bootstraps the React application into the browser document.
 *
 * The provider tree is centralized here so later runtime concerns can be added once and
 * inherited by every route, shell component, and feature slice.
 */
function bootstrapApplication(): void {
  // React StrictMode remains enabled in development so future components surface unsafe
  // lifecycle assumptions before they reach the wider workbench implementation.
  createRoot(getRequiredRootElement()).render(
    <StrictMode>
      <ApplicationProviders>
        <App />
      </ApplicationProviders>
    </StrictMode>,
  );
}

bootstrapApplication();
