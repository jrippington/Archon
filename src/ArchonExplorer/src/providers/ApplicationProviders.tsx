import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

/**
 * Creates the shared TanStack Query client for the ArchonExplorer runtime.
 *
 * The factory keeps server-state defaults in one location so later API work can tune
 * caching, retry, and polling behavior without updating feature components one by one.
 * Work Item 1 does not execute queries, so the defaults are intentionally conservative.
 *
 * @returns A QueryClient instance used by the application-level provider tree.
 */
function createQueryClient(): QueryClient {
  // A single client instance is created for the browser runtime to prevent cache loss
  // between component renders while still allowing this setup to be tested or replaced.
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        refetchOnWindowFocus: false,
      },
    },
  });
}

// The module-level QueryClient mirrors normal browser application lifetime: one client is
// shared for the current page load and disposed naturally when the document unloads.
const queryClient = createQueryClient();

/**
 * Provides application-wide runtime services to the React component tree.
 *
 * @param props Contains the descendant React nodes that require shared providers.
 * @param props.children The application tree rendered inside the configured providers.
 * @returns The provider-wrapped application tree.
 */
export function ApplicationProviders({ children }: { children: ReactNode }) {
  // TanStack Query is installed during the skeleton slice so future server-state features
  // inherit one cache and one policy surface instead of introducing competing patterns.
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
