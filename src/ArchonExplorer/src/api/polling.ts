import type { ArchonApiClientRequestControls } from './archonApiClient';
import type { ExtractionRunStatusResponse, NormalizedArchonApiError } from './archonApiTypes';
import type { ArchonApiRequestResult } from './request';

/**
 * Names the normalized polling states that ArchonExplorer exposes for extraction runs.
 */
export type ExtractionRunPollingState = 'idle' | 'polling' | 'completed' | 'failed' | 'canceled' | 'unavailable' | 'unknown' | 'stalled' | 'cancelled';

/**
 * Names extraction run status values that stop polling because no later status is expected.
 */
export type ExtractionRunTerminalStatus = 'completed' | 'failed' | 'canceled' | 'cancelled' | 'unavailable' | 'unknown';

/**
 * Describes the minimal operational client surface needed by extraction polling.
 */
export interface ExtractionRunPollingClient {
  /**
   * Reads the current status for one extraction run.
   *
   * @param runId - The public extraction run identifier being polled.
   * @param controls - Optional cancellation and timeout controls supplied by TanStack Query or another caller.
   * @returns The typed run status result or a normalized safe failure.
   */
  getExtractionStatus(runId: string, controls?: ArchonApiClientRequestControls): Promise<ArchonApiRequestResult<ExtractionRunStatusResponse>>;
}

/**
 * Describes configuration values that bound extraction polling behavior.
 */
export interface ExtractionPollingOptions {
  /**
   * Selects the starting delay between accepted/running status checks.
   */
  readonly initialIntervalMs?: number;

  /**
   * Selects the largest delay allowed between status checks.
   */
  readonly maxIntervalMs?: number;

  /**
   * Selects the incremental delay added after each non-terminal status check.
   */
  readonly backoffStepMs?: number;

  /**
   * Selects the number of non-terminal checks allowed before the operation is treated as stalled.
   */
  readonly maxAttempts?: number;

  /**
   * Selects the total elapsed polling time allowed before the operation is treated as stalled.
   */
  readonly stalledAfterMs?: number;
}

/**
 * Describes the elapsed polling context used to calculate the next interval.
 */
export interface ExtractionPollingIntervalInput {
  /**
   * Counts completed polling attempts for the current run.
   */
  readonly attempt: number;

  /**
   * Supplies optional interval bounds and backoff settings.
   */
  readonly options?: ExtractionPollingOptions;
}

/**
 * Describes the input used to derive polling state from a status response or safe error.
 */
export interface ExtractionPollingStateInput {
  /**
   * Supplies the latest successful extraction status when a poll completed normally.
   */
  readonly status?: ExtractionRunStatusResponse;

  /**
   * Supplies the safe normalized failure when a poll failed.
   */
  readonly error?: NormalizedArchonApiError;

  /**
   * Indicates that caller-owned cancellation has stopped the polling workflow.
   */
  readonly cancelled?: boolean;

  /**
   * Supplies the number of completed attempts for stalled-operation detection.
   */
  readonly attempt?: number;

  /**
   * Supplies elapsed milliseconds for stalled-operation detection.
   */
  readonly elapsedMs?: number;

  /**
   * Supplies optional polling bounds used by stalled-operation detection.
   */
  readonly options?: ExtractionPollingOptions;
}

/**
 * Describes one polling step result that can drive hooks or non-React tests.
 */
export interface ExtractionPollingStepResult {
  /**
   * Contains the safe machine-readable polling state after the step.
   */
  readonly state: ExtractionRunPollingState;

  /**
   * Indicates whether another status check should be scheduled.
   */
  readonly continuePolling: boolean;

  /**
   * Contains the next delay in milliseconds when polling should continue.
   */
  readonly nextIntervalMs?: number;

  /**
   * Contains the latest successful status response when one was returned.
   */
  readonly status?: ExtractionRunStatusResponse;

  /**
   * Contains a safe error when the status check failed.
   */
  readonly error?: NormalizedArchonApiError;
}

/**
 * Default bounded polling settings used by extraction run helpers.
 */
export const defaultExtractionPollingOptions = {
  /** Initial delay avoids tight loops immediately after run acceptance. */
  initialIntervalMs: 2_000,
  /** Maximum delay keeps long-running operations observable without refetch storms. */
  maxIntervalMs: 15_000,
  /** Linear backoff keeps behavior predictable for tests and contributors. */
  backoffStepMs: 1_000,
  /** Attempt bound prevents endless client-side polling when a status never reaches a terminal state. */
  maxAttempts: 60,
  /** Time bound reports stalled state after roughly five minutes of polling. */
  stalledAfterMs: 300_000,
} as const satisfies Required<ExtractionPollingOptions>;

/**
 * Determines whether an extraction run status name is terminal.
 *
 * @param status - The raw status text returned by the API.
 * @returns True when polling should stop for the supplied status.
 */
export function isExtractionRunTerminalStatus(status: string): boolean {
  // Status names are normalized case-insensitively because backend enum casing may
  // differ from display text while still representing the same workflow state.
  return toTerminalStatus(status) !== undefined;
}

/**
 * Converts a raw API status value into a terminal status name when possible.
 *
 * @param status - The raw status text returned by the API.
 * @returns The normalized terminal status, or undefined for active/non-terminal statuses.
 */
export function toTerminalStatus(status: string): ExtractionRunTerminalStatus | undefined {
  // Both American and British cancellation spellings are accepted so the UI remains
  // tolerant if backend vocabulary changes between canceled and cancelled.
  const normalized = status.trim().toLowerCase();
  if (normalized === 'completed' || normalized === 'succeeded' || normalized === 'success') {
    return 'completed';
  }

  if (normalized === 'failed' || normalized === 'failure' || normalized === 'faulted') {
    return 'failed';
  }

  if (normalized === 'canceled') {
    return 'canceled';
  }

  if (normalized === 'cancelled') {
    return 'cancelled';
  }

  if (normalized === 'unavailable') {
    return 'unavailable';
  }

  if (normalized === 'unknown') {
    return 'unknown';
  }

  return undefined;
}

/**
 * Calculates the next bounded polling interval.
 *
 * @param input - The completed attempt count and optional polling bounds.
 * @returns The delay in milliseconds before the next polling attempt.
 */
export function calculateExtractionPollingInterval(input: ExtractionPollingIntervalInput): number {
  // Linear backoff is deliberately simple: it avoids tight loops, is easy to reason
  // about in tests, and never exceeds the configured maximum interval.
  const options = mergePollingOptions(input.options);
  const attempt = Math.max(0, input.attempt);
  const interval = options.initialIntervalMs + attempt * options.backoffStepMs;
  return Math.min(interval, options.maxIntervalMs);
}

/**
 * Determines whether polling should be considered stalled.
 *
 * @param input - Attempt count, elapsed time, and optional bounds for the polling workflow.
 * @returns True when polling exceeded either configured stalled bound.
 */
export function isExtractionPollingStalled(input: Pick<ExtractionPollingStateInput, 'attempt' | 'elapsedMs' | 'options'>): boolean {
  // Either bound is enough to prevent a stuck browser session from polling forever.
  const options = mergePollingOptions(input.options);
  const attempt = input.attempt ?? 0;
  const elapsedMs = input.elapsedMs ?? 0;
  return attempt >= options.maxAttempts || elapsedMs >= options.stalledAfterMs;
}

/**
 * Derives a safe polling state from a successful status, normalized failure, or cancellation flag.
 *
 * @param input - The latest status/failure plus optional attempt and elapsed timing context.
 * @returns The machine-readable polling state for UI and hook consumers.
 */
export function deriveExtractionPollingState(input: ExtractionPollingStateInput): ExtractionRunPollingState {
  // Cancellation and stalled bounds take precedence because they describe the client
  // workflow itself, while status/error values describe a single API response.
  if (input.cancelled === true) {
    return 'cancelled';
  }

  if (isExtractionPollingStalled(input)) {
    return 'stalled';
  }

  if (input.error !== undefined) {
    return mapPollingError(input.error);
  }

  if (input.status === undefined) {
    return 'idle';
  }

  const terminalStatus = toTerminalStatus(input.status.status);
  if (terminalStatus === 'completed') {
    return 'completed';
  }

  if (terminalStatus === 'failed') {
    return 'failed';
  }

  if (terminalStatus === 'canceled' || terminalStatus === 'cancelled') {
    return 'canceled';
  }

  if (terminalStatus === 'unavailable') {
    return 'unavailable';
  }

  if (terminalStatus === 'unknown') {
    return 'unknown';
  }

  return 'polling';
}

/**
 * Executes one extraction polling status check and reports whether polling should continue.
 *
 * @param client - The operational client that owns the typed status request.
 * @param runId - The public run identifier being polled.
 * @param attempt - The completed attempt count before this step.
 * @param startedAtMs - The timestamp when polling began, expressed in the same clock as `nowMs`.
 * @param nowMs - The current timestamp used for elapsed-time checks.
 * @param options - Optional polling bounds and interval settings.
 * @param signal - Optional cancellation signal from the caller or TanStack Query.
 * @returns A polling step result containing safe state, status/error data, and the next interval.
 */
export async function pollExtractionRunStatus(
  client: ExtractionRunPollingClient,
  runId: string,
  attempt: number,
  startedAtMs: number,
  nowMs: number,
  options?: ExtractionPollingOptions,
  signal?: AbortSignal,
): Promise<ExtractionPollingStepResult> {
  // The helper performs only one status request. Scheduling remains the responsibility
  // of TanStack Query or a caller-owned loop so cancellation and UI lifetimes stay clear.
  if (signal?.aborted === true) {
    return { state: 'cancelled', continuePolling: false };
  }

  if (isExtractionPollingStalled({ attempt, elapsedMs: nowMs - startedAtMs, options })) {
    return { state: 'stalled', continuePolling: false };
  }

  const result = await client.getExtractionStatus(runId, { signal });
  if (!result.ok) {
    const state = deriveExtractionPollingState({ error: result.error, attempt, elapsedMs: nowMs - startedAtMs, options });
    return { state, continuePolling: false, error: result.error };
  }

  const state = deriveExtractionPollingState({ status: result.data, attempt, elapsedMs: nowMs - startedAtMs, options });
  const continuePolling = state === 'polling';
  return {
    state,
    continuePolling,
    nextIntervalMs: continuePolling ? calculateExtractionPollingInterval({ attempt: attempt + 1, options }) : undefined,
    status: result.data,
  };
}

/**
 * Merges caller-provided polling options with safe runtime defaults.
 *
 * @param options - Optional partial polling bounds selected by a hook or test.
 * @returns A complete option object with non-negative bounded values.
 */
export function mergePollingOptions(options: ExtractionPollingOptions = {}): Required<ExtractionPollingOptions> {
  // Values are clamped to safe minimums so invalid caller input cannot create a zero
  // interval loop or disable stalled-operation detection accidentally.
  return {
    initialIntervalMs: Math.max(250, options.initialIntervalMs ?? defaultExtractionPollingOptions.initialIntervalMs),
    maxIntervalMs: Math.max(250, options.maxIntervalMs ?? defaultExtractionPollingOptions.maxIntervalMs),
    backoffStepMs: Math.max(0, options.backoffStepMs ?? defaultExtractionPollingOptions.backoffStepMs),
    maxAttempts: Math.max(1, options.maxAttempts ?? defaultExtractionPollingOptions.maxAttempts),
    stalledAfterMs: Math.max(1_000, options.stalledAfterMs ?? defaultExtractionPollingOptions.stalledAfterMs),
  };
}

/**
 * Converts a normalized API failure into a safe polling state.
 *
 * @param error - The safe normalized error returned by the request foundation.
 * @returns A polling state that does not expose raw backend diagnostics.
 */
function mapPollingError(error: NormalizedArchonApiError): ExtractionRunPollingState {
  // Network, timeout, and not-found are treated as unavailable from the polling
  // perspective; destructive mutation retry logic is intentionally not involved here.
  if (error.category === 'cancelled') {
    return 'cancelled';
  }

  if (error.category === 'network' || error.category === 'timeout' || error.category === 'notFound') {
    return 'unavailable';
  }

  return 'unknown';
}