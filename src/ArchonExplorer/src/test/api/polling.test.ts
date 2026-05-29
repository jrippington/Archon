import { describe, expect, it, vi } from 'vitest';
import type { ExtractionRunStatusResponse, NormalizedArchonApiError } from '@/api/archonApiTypes';
import {
  calculateExtractionPollingInterval,
  deriveExtractionPollingState,
  isExtractionPollingStalled,
  isExtractionRunTerminalStatus,
  pollExtractionRunStatus,
  toTerminalStatus,
} from '@/api/polling';
import { createExtractionRunStatus, failure, ok } from '@/api/testDoubles';
import type { ArchonApiRequestResult } from '@/api/request';

/**
 * Creates a fake polling client that returns one supplied request result.
 *
 * @param result - The status result returned by the fake client.
 * @returns A client with a Vitest-tracked status method.
 */
function createPollingClient(result: ArchonApiRequestResult<ExtractionRunStatusResponse>) {
  // Polling tests focus on helper behavior, so the fake implements only the minimal
  // typed client method required by the polling boundary.
  return {
    getExtractionStatus: vi.fn().mockResolvedValue(result),
  };
}

/**
 * Creates a normalized error for direct state derivation tests.
 *
 * @param category - The safe normalized error category to expose.
 * @returns A normalized error with safe text only.
 */
function normalizedError(category: NormalizedArchonApiError['category']): NormalizedArchonApiError {
  // The error deliberately contains no raw URL, stack trace, or backend detail.
  return { category, message: 'Safe polling error.', retryable: category !== 'cancelled' };
}

/**
 * Verifies extraction run terminal status normalization.
 */
describe('extraction polling terminal statuses', () => {
  /**
   * Confirms completed, failed, canceled, unavailable, and unknown values stop polling.
   */
  it('recognizes terminal status values case-insensitively', () => {
    expect(isExtractionRunTerminalStatus('Completed')).toBe(true);
    expect(toTerminalStatus('FAILED')).toBe('failed');
    expect(toTerminalStatus('Cancelled')).toBe('cancelled');
    expect(toTerminalStatus('Unavailable')).toBe('unavailable');
    expect(toTerminalStatus('Unknown')).toBe('unknown');
    expect(isExtractionRunTerminalStatus('Running')).toBe(false);
  });
});

/**
 * Verifies bounded intervals and stalled-operation detection.
 */
describe('extraction polling interval and stalled bounds', () => {
  /**
   * Confirms interval backoff is bounded by the configured maximum.
   */
  it('calculates bounded polling intervals', () => {
    const options = { initialIntervalMs: 1_000, backoffStepMs: 500, maxIntervalMs: 2_000 };

    expect(calculateExtractionPollingInterval({ attempt: 0, options })).toBe(1_000);
    expect(calculateExtractionPollingInterval({ attempt: 2, options })).toBe(2_000);
    expect(calculateExtractionPollingInterval({ attempt: 10, options })).toBe(2_000);
  });

  /**
   * Confirms attempt and elapsed-time bounds both report stalled state.
   */
  it('detects stalled polling by attempts or elapsed time', () => {
    expect(isExtractionPollingStalled({ attempt: 3, elapsedMs: 100, options: { maxAttempts: 3, stalledAfterMs: 1_000 } })).toBe(true);
    expect(isExtractionPollingStalled({ attempt: 1, elapsedMs: 1_000, options: { maxAttempts: 3, stalledAfterMs: 1_000 } })).toBe(true);
    expect(isExtractionPollingStalled({ attempt: 1, elapsedMs: 100, options: { maxAttempts: 3, stalledAfterMs: 1_000 } })).toBe(false);
  });
});

/**
 * Verifies safe polling state derivation from statuses, failures, cancellation, and stalls.
 */
describe('deriveExtractionPollingState', () => {
  /**
   * Confirms active statuses continue polling while terminal statuses map to final states.
   */
  it('maps active and terminal API statuses to polling states', () => {
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Running' }) })).toBe('polling');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Completed' }) })).toBe('completed');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Failed' }) })).toBe('failed');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Canceled' }) })).toBe('canceled');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Unavailable' }) })).toBe('unavailable');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Unknown' }) })).toBe('unknown');
  });

  /**
   * Confirms cancellation and stalled detection stop polling before interpreting a response.
   */
  it('prioritizes cancellation and stalled states', () => {
    expect(deriveExtractionPollingState({ cancelled: true, status: createExtractionRunStatus({ status: 'Running' }) })).toBe('cancelled');
    expect(deriveExtractionPollingState({ attempt: 2, elapsedMs: 100, options: { maxAttempts: 2 }, status: createExtractionRunStatus({ status: 'Running' }) })).toBe('stalled');
  });

  /**
   * Confirms normalized failures become safe unavailable, cancelled, or unknown states.
   */
  it('maps safe error categories to polling states', () => {
    expect(deriveExtractionPollingState({ error: normalizedError('network') })).toBe('unavailable');
    expect(deriveExtractionPollingState({ error: normalizedError('timeout') })).toBe('unavailable');
    expect(deriveExtractionPollingState({ error: normalizedError('cancelled') })).toBe('cancelled');
    expect(deriveExtractionPollingState({ error: normalizedError('server') })).toBe('unknown');
  });
});

/**
 * Verifies the one-step polling helper used by hooks and non-React consumers.
 */
describe('pollExtractionRunStatus', () => {
  /**
   * Confirms active status responses schedule another bounded polling interval.
   */
  it('continues polling for non-terminal statuses', async () => {
    const client = createPollingClient(ok(createExtractionRunStatus({ status: 'Running' })));

    const result = await pollExtractionRunStatus(client, 'run-1', 0, 0, 500, { initialIntervalMs: 1_000, backoffStepMs: 100, maxIntervalMs: 2_000 });

    expect(result.state).toBe('polling');
    expect(result.continuePolling).toBe(true);
    expect(result.nextIntervalMs).toBe(1_100);
    expect(client.getExtractionStatus).toHaveBeenCalledWith('run-1', { signal: undefined });
  });

  /**
   * Confirms terminal status responses stop polling immediately.
   */
  it('stops polling for terminal statuses', async () => {
    const client = createPollingClient(ok(createExtractionRunStatus({ status: 'Completed' })));

    const result = await pollExtractionRunStatus(client, 'run-1', 0, 0, 500);

    expect(result.state).toBe('completed');
    expect(result.continuePolling).toBe(false);
    expect(result.nextIntervalMs).toBeUndefined();
  });

  /**
   * Confirms aborted signals stop polling without calling the client.
   */
  it('stops polling when already cancelled', async () => {
    const client = createPollingClient(ok(createExtractionRunStatus({ status: 'Running' })));
    const controller = new AbortController();
    controller.abort();

    const result = await pollExtractionRunStatus(client, 'run-1', 0, 0, 500, undefined, controller.signal);

    expect(result.state).toBe('cancelled');
    expect(result.continuePolling).toBe(false);
    expect(client.getExtractionStatus).not.toHaveBeenCalled();
  });

  /**
   * Confirms stalled bounds stop polling without issuing another status request.
   */
  it('stops polling when stalled before the next request', async () => {
    const client = createPollingClient(ok(createExtractionRunStatus({ status: 'Running' })));

    const result = await pollExtractionRunStatus(client, 'run-1', 3, 0, 100, { maxAttempts: 3 });

    expect(result.state).toBe('stalled');
    expect(result.continuePolling).toBe(false);
    expect(client.getExtractionStatus).not.toHaveBeenCalled();
  });

  /**
   * Confirms safe API failures map to a non-continuing polling state.
   */
  it('stops polling for safe failure results', async () => {
    const client = createPollingClient(failure('notFound', 'Extraction run was not found.', false, 404));

    const result = await pollExtractionRunStatus(client, 'missing-run', 0, 0, 500);

    expect(result.state).toBe('unavailable');
    expect(result.continuePolling).toBe(false);
    expect(result.error?.category).toBe('notFound');
  });
});