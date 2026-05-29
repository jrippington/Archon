import { queryOptions, type UseQueryOptions } from '@tanstack/react-query';
import { ArchonApiClient, type ExtractionRunHistoryQuery } from '@/api/archonApiClient';
import type { ExtractionRunHistoryResponse, NormalizedArchonApiError } from '@/api/archonApiTypes';
import { archonQueryKeys, type ArchonQueryKey } from '@/api/queryKeys';

/**
 * Represents the typed client surface required by the extraction history query hook.
 */
export type ExtractionHistoryClient = Pick<ArchonApiClient, 'getExtractionHistory'>;

/**
 * Describes the inputs accepted by the Extraction Center history hook.
 */
export interface UseExtractionHistoryOptions extends ExtractionRunHistoryQuery {
  /**
   * Supplies a production client or deterministic test double for the history request.
   */
  readonly client?: ExtractionHistoryClient;
}

/**
 * Represents a safe page-level error produced when extraction history cannot be loaded.
 */
export class ExtractionHistoryError extends Error {
  /**
   * Classifies the failure without exposing backend, transport, or database internals.
   */
  public readonly category: NormalizedArchonApiError['category'];

  /**
   * Indicates whether the query is safe for the user to retry.
   */
  public readonly retryable: boolean;

  /**
   * Carries the HTTP status code when the normalized API failure included one.
   */
  public readonly status?: number;

  /**
   * Initializes a safe history error from an already-normalized API error.
   *
   * @param error The normalized API error returned by the shared request foundation.
   */
  public constructor(error: NormalizedArchonApiError) {
    // The superclass receives only the sanitized user-visible message. Raw exception text,
    // route details, driver diagnostics, and backend internals are never added here.
    super(error.message);
    this.name = 'ExtractionHistoryError';
    this.category = error.category;
    this.retryable = error.retryable;
    this.status = error.status;
  }
}

/**
 * Creates TanStack Query options for the recent extraction history request.
 *
 * @param options Optional history bounds and client override used by production UI and tests.
 * @returns Query options that load `GET /extractions` through the typed API client.
 */
export function useExtractionHistory(options: UseExtractionHistoryOptions = {}): UseQueryOptions<ExtractionRunHistoryResponse, ExtractionHistoryError, ExtractionRunHistoryResponse, ArchonQueryKey> {
  // The function returns query options instead of invoking useQuery directly so tests can
  // exercise the query with QueryClient.fetchQuery while components can pass the same object
  // to useQuery. Server state remains in TanStack Query and is not copied into local state.
  const { client = new ArchonApiClient(), take } = options;
  const query: ExtractionRunHistoryQuery = { take };

  return queryOptions({
    queryKey: archonQueryKeys.extraction.history(query),
    queryFn: async ({ signal }) => {
      // TanStack supplies an AbortSignal that the typed client forwards to fetch, allowing
      // unmounted components or superseded requests to cancel without leaving work in flight.
      const result = await client.getExtractionHistory(query, { signal });
      if (!result.ok) {
        throw new ExtractionHistoryError(result.error);
      }

      return result.data;
    },
  });
}
