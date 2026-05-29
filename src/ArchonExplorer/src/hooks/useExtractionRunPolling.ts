import { useMemo } from 'react';
import { useQuery, type QueryKey } from '@tanstack/react-query';
import { archonApiClient, type ArchonApiClient } from '@/api/archonApiClient';
import type { ExtractionRunStatusResponse, NormalizedArchonApiError } from '@/api/archonApiTypes';
import {
  calculateExtractionPollingInterval,
  deriveExtractionPollingState,
  mergePollingOptions,
  type ExtractionPollingOptions,
  type ExtractionRunPollingState,
} from '@/api/polling';
import { archonQueryKeys } from '@/api/queryKeys';

/**
 * Describes optional dependencies and polling bounds for extraction run polling.
 */
export interface UseExtractionRunPollingOptions {
  /**
   * Supplies the public extraction run identifier to poll; absence disables polling.
   */
  readonly runId?: string;

  /**
   * Supplies the typed operational client used to read extraction run status.
   */
  readonly client?: Pick<ArchonApiClient, 'getExtractionStatus'>;

  /**
   * Enables or disables polling without losing the configured run identifier.
   */
  readonly enabled?: boolean;

  /**
   * Supplies bounded interval, attempt, and stalled-state settings.
   */
  readonly polling?: ExtractionPollingOptions;
}

/**
 * Describes the status, data, and control metadata returned by extraction polling.
 */
export interface UseExtractionRunPollingResult {
  /**
   * Contains the safe machine-readable polling state for UI consumers.
   */
  readonly state: ExtractionRunPollingState;

  /**
   * Contains the latest successful status response when one is available.
   */
  readonly status?: ExtractionRunStatusResponse;

  /**
   * Contains the latest safe polling error when the status request failed.
   */
  readonly error?: NormalizedArchonApiError;

  /**
   * Indicates whether TanStack Query is currently fetching a status update.
   */
  readonly isFetching: boolean;

  /**
   * Indicates whether another automatic status check is expected.
   */
  readonly continuePolling: boolean;

  /**
   * Contains the active query key so callers and tests can inspect cache identity.
   */
  readonly queryKey: ReturnType<typeof archonQueryKeys.extraction.run> | typeof archonQueryKeys.extraction.runs;
}

/**
 * Polls one extraction run status through TanStack Query without implementing a feature screen.
 *
 * @param options - Run identifier, optional client override, enablement flag, and polling bounds.
 * @returns The current polling state, latest status, safe error, fetch flag, continuation flag, and query key.
 */
export function useExtractionRunPolling(options: UseExtractionRunPollingOptions): UseExtractionRunPollingResult {
  // The hook proves the runtime pattern for later Extraction Center work while keeping
  // scheduling inside TanStack Query and avoiding any UI feature implementation here.
  const client = options.client ?? archonApiClient;
  const pollingOptions = useMemo(() => mergePollingOptions(options.polling), [options.polling]);
  const enabled = options.enabled !== false && options.runId !== undefined && options.runId.trim().length > 0;
  const queryKey = options.runId === undefined ? archonQueryKeys.extraction.runs : archonQueryKeys.extraction.run({ runId: options.runId });
  const tanStackQueryKey: QueryKey = queryKey;

  const query = useQuery<ExtractionRunStatusResponse, NormalizedArchonApiError, ExtractionRunStatusResponse, QueryKey>({
    queryKey: tanStackQueryKey,
    enabled,
    retry: false,
    refetchOnWindowFocus: false,
    refetchInterval: (queryState) => {
      // TanStack Query asks for the next interval after each result. Terminal and
      // stalled states return false so automatic polling stops deterministically.
      const status = queryState.state.data;
      const state = status === undefined ? 'polling' : deriveExtractionPollingState({ status, options: pollingOptions });
      return state === 'polling' ? calculateExtractionPollingInterval({ attempt: 1, options: pollingOptions }) : false;
    },
    queryFn: async ({ signal }) => {
      // The query cannot run unless `enabled` proved a run ID exists, but the explicit
      // guard keeps TypeScript and future maintainers aligned with that invariant.
      if (options.runId === undefined) {
        throw { category: 'configuration', message: 'Extraction run polling requires a run identifier.', retryable: false } satisfies NormalizedArchonApiError;
      }

      const result = await client.getExtractionStatus(options.runId, { signal });
      if (!result.ok) {
        throw result.error;
      }

      return result.data;
    },
  });

  const state = deriveExtractionPollingState({ status: query.data, error: query.error ?? undefined, options: pollingOptions });
  return {
    state: enabled ? state : 'idle',
    status: query.data,
    error: query.error ?? undefined,
    isFetching: query.isFetching,
    continuePolling: enabled && state === 'polling',
    queryKey,
  };
}