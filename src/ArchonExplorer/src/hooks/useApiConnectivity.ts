import { useQuery } from '@tanstack/react-query';
import { archonApiClient, type ArchonApiClient } from '@/api/archonApiClient';
import { checkApiConnectivity, createCheckingConnectivityState, deriveConnectivityState, type ApiConnectivityState } from '@/api/connectivity';
import { getApiConfiguration, type ApiConfiguration } from '@/config/apiConfiguration';

/**
 * Describes optional dependencies for the API connectivity hook.
 */
export interface UseApiConnectivityOptions {
  /**
   * Supplies safe API base URL configuration; production reads the Vite adapter when omitted.
   */
  readonly apiConfiguration?: ApiConfiguration;

  /**
   * Supplies the typed operational client used for health and readiness probes.
   */
  readonly client?: Pick<ArchonApiClient, 'getHealth' | 'getReadiness'>;
}

/**
 * Names the shared TanStack Query key for global API connectivity checks.
 */
const apiConnectivityQueryKey = ['archonApi', 'connectivity'] as const;

/**
 * Returns safe global API connectivity state for shell and future diagnostics UI.
 *
 * @param options - Optional configuration and client overrides for tests or specialized hosts.
 * @returns A safe connectivity state with machine-readable status and accessible text.
 */
export function useApiConnectivity(options: UseApiConnectivityOptions = {}): ApiConnectivityState {
  // The hook reads configuration first so an absent API base URL short-circuits without
  // issuing health/readiness requests that would produce noisy or misleading failures.
  const apiConfiguration = options.apiConfiguration ?? getApiConfiguration();
  const client = options.client ?? archonApiClient;

  const query = useQuery({
    queryKey: apiConnectivityQueryKey,
    enabled: apiConfiguration.isConfigured,
    retry: false,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      // The pure helper owns sequential health/readiness behavior so hook and unit
      // tests exercise the same connectivity derivation path.
      return checkApiConnectivity(client, apiConfiguration, signal);
    },
  });

  if (!apiConfiguration.isConfigured) {
    return deriveConnectivityState({ apiConfiguration });
  }

  if (query.isPending || query.isFetching) {
    return createCheckingConnectivityState();
  }

  if (query.data !== undefined) {
    return query.data;
  }

  return deriveConnectivityState({ apiConfiguration });
}

/**
 * Gets the query key used for global API connectivity state.
 *
 * @returns The stable query key consumed by tests and future invalidation helpers.
 */
export function getApiConnectivityQueryKey(): typeof apiConnectivityQueryKey {
  // Exposing the key prevents future components from duplicating literal query-key arrays.
  return apiConnectivityQueryKey;
}
