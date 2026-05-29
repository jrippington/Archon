import { useMutation, useQueryClient, type UseMutationOptions, type UseMutationResult } from '@tanstack/react-query';
import { ArchonApiClient } from '@/api/archonApiClient';
import type { ExtractionRunStatusResponse, NormalizedArchonApiError, StartExtractionRequest } from '@/api/archonApiTypes';
import { getExtractionInvalidationKeys } from '@/api/queryKeys';

/**
 * Represents the typed client surface required by the start-extraction mutation hook.
 */
export type StartExtractionClient = Pick<ArchonApiClient, 'startExtraction'>;

/**
 * Describes optional dependencies and callbacks accepted by the start-extraction mutation hook.
 */
export interface UseStartExtractionOptions {
  /**
   * Supplies a production client or deterministic test double for the start request.
   */
  readonly client?: StartExtractionClient;

  /**
   * Receives the accepted run response after cache invalidation has been requested.
   */
  readonly onAccepted?: (run: ExtractionRunStatusResponse) => void;
}

/**
 * Represents a safe mutation error produced when extraction start cannot be accepted.
 */
export class StartExtractionError extends Error {
  /**
   * Classifies the failure without exposing backend, transport, or database internals.
   */
  public readonly category: NormalizedArchonApiError['category'];

  /**
   * Indicates whether the mutation is safe for the user to retry after correction or setup changes.
   */
  public readonly retryable: boolean;

  /**
   * Carries the HTTP status code when the normalized API failure included one.
   */
  public readonly status?: number;

  /**
   * Contains safe validation issues when the API rejected submitted fields.
   */
  public readonly validationIssues?: NormalizedArchonApiError['validationIssues'];

  /**
   * Initializes a safe start-extraction error from an already-normalized API error.
   *
   * @param error The normalized API error returned by the shared request foundation.
   */
  public constructor(error: NormalizedArchonApiError) {
    // The superclass receives only sanitized copy. Raw exception text, route details,
    // configured URLs, and backend diagnostics are never attached to this error object.
    super(error.message);
    this.name = 'StartExtractionError';
    this.category = error.category;
    this.retryable = error.retryable;
    this.status = error.status;
    this.validationIssues = error.validationIssues;
  }
}

/**
 * Creates a TanStack mutation for submitting POST /extractions through the typed client.
 *
 * @param options Optional client and accepted-run callback used by production UI and tests.
 * @returns A mutation result that accepts typed start-extraction request bodies.
 */
export function useStartExtraction(options: UseStartExtractionOptions = {}): UseMutationResult<ExtractionRunStatusResponse, StartExtractionError, StartExtractionRequest> {
  // The hook owns mutation orchestration only. Form state remains in the component, while
  // server state is refreshed through TanStack Query invalidation after an accepted response.
  const { client = new ArchonApiClient(), onAccepted } = options;
  const queryClient = useQueryClient();

  const mutationOptions: UseMutationOptions<ExtractionRunStatusResponse, StartExtractionError, StartExtractionRequest> = {
    mutationFn: async (request) => {
      // The typed operational client owns route selection, JSON serialization, base URL handling,
      // cancellation, and safe error shaping, so feature components never call fetch directly.
      const result = await client.startExtraction(request);
      if (!result.ok) {
        throw new StartExtractionError(result.error);
      }

      return result.data;
    },
    onSuccess: (run) => {
      // Starting a run can change recent history immediately and can also prime later selected-run
      // status reads, so both broad history and exact run selectors are invalidated deliberately.
      const invalidationKeys = getExtractionInvalidationKeys(run.runId);
      void queryClient.invalidateQueries({ queryKey: invalidationKeys.histories });
      if (invalidationKeys.run !== undefined) {
        void queryClient.invalidateQueries({ queryKey: invalidationKeys.run });
      }

      onAccepted?.(run);
    },
  };

  return useMutation(mutationOptions);
}
