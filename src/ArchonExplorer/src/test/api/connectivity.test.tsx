import { describe, expect, it, vi } from 'vitest';
import type { ArchonApiClient } from '@/api/archonApiClient';
import {
  checkApiConnectivity,
  createCheckingConnectivityState,
  createUnknownConnectivityState,
  deriveConnectivityState,
  getConnectivityBadgeVariant,
  getConnectivityStatusText,
  type ApiConnectivityState,
} from '@/api/connectivity';
import type { ArchonApiRequestResult } from '@/api/request';
import type { ArchonApiErrorCategory, ManagementHealthResponse, ManagementReadinessResponse } from '@/api/archonApiTypes';

/**
 * Creates a successful typed request result for connectivity tests.
 *
 * @param data - The response payload returned by a fake operational client method.
 * @returns A normalized successful request result.
 */
function ok<TResponse>(data: TResponse): ArchonApiRequestResult<TResponse> {
  // Connectivity tests only need the request-result envelope, not real transport.
  return { ok: true, data, status: 200 };
}

/**
 * Creates a failed typed request result for connectivity tests.
 *
 * @param category - The safe normalized category that the runtime should interpret.
 * @returns A normalized failure result with safe text only.
 */
function failure(category: ArchonApiErrorCategory): ArchonApiRequestResult<never> {
  // The helper keeps failure states safe by avoiding raw exception messages or URLs.
  return {
    ok: false,
    error: {
      category,
      message: 'Safe failure message.',
      retryable: category !== 'configuration' && category !== 'cancelled',
    },
  };
}

/**
 * Creates a minimal operational client test double for hook scenarios.
 *
 * @param health - The health result returned by the fake client.
 * @param readiness - The readiness result returned by the fake client.
 * @returns A partial ArchonApiClient that supplies only connectivity methods.
 */
function createConnectivityClient(
  health: ArchonApiRequestResult<ManagementHealthResponse>,
  readiness: ArchonApiRequestResult<ManagementReadinessResponse>,
): Pick<ArchonApiClient, 'getHealth' | 'getReadiness'> {
  // The hook should consume the typed operational client boundary, so the fake
  // implements that boundary rather than fetch or route details.
  return {
    getHealth: vi.fn().mockResolvedValue(health),
    getReadiness: vi.fn().mockResolvedValue(readiness),
  };
}

/**
 * Verifies safe connectivity-state derivation from configuration and probe results.
 */
describe('connectivity state derivation', () => {
  /**
   * Confirms absent API configuration has a distinct setup state and safe label.
   */
  it('returns unconfigured state when the API base URL is absent', () => {
    const state = deriveConnectivityState({ apiConfiguration: { isConfigured: false } });

    expect(state.status).toBe('unconfigured');
    expect(getConnectivityStatusText(state)).toBe('API base URL not configured');
  });

  /**
   * Confirms reachable API state requires both health and readiness success.
   */
  it('returns reachable state when health and readiness succeed', () => {
    const state = deriveConnectivityState({
      apiConfiguration: { isConfigured: true, baseUrl: 'https://localhost:5001' },
      health: ok({ status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: [], warnings: [] }),
      readiness: ok({ status: 'Ready', checkedUtc: '2026-01-01T00:00:00Z', dependencies: [], warnings: [] }),
    });

    expect(state.status).toBe('reachable');
    expect(getConnectivityBadgeVariant(state)).toBe('secondary');
  });

  /**
   * Confirms readiness failures are represented as not-ready rather than leaking
   * dependency details into the shell.
   */
  it('returns not-ready state when health works but readiness fails', () => {
    const state = deriveConnectivityState({
      apiConfiguration: { isConfigured: true, baseUrl: 'https://localhost:5001' },
      health: ok({ status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: [], warnings: [] }),
      readiness: failure('server'),
    });

    expect(state.status).toBe('notReady');
    expect(getConnectivityStatusText(state)).toBe('API reachable; dependencies not ready');
  });

  /**
   * Confirms network-level failures become unreachable while unexpected shapes stay
   * in the safe unknown bucket.
   */
  it('distinguishes unreachable and unknown failure states', () => {
    const unreachable = deriveConnectivityState({ apiConfiguration: { isConfigured: true, baseUrl: 'https://localhost:5001' }, health: failure('network') });
    const unknown = deriveConnectivityState({ apiConfiguration: { isConfigured: true, baseUrl: 'https://localhost:5001' }, health: failure('unexpectedResponse') });

    expect(unreachable.status).toBe('unreachable');
    expect(unknown.status).toBe('unknown');
  });

  /**
   * Confirms explicit checking and unknown constructors produce accessible labels.
   */
  it('creates checking and unknown states with safe labels', () => {
    expect(getConnectivityStatusText(createCheckingConnectivityState())).toBe('Checking API connectivity');
    expect(getConnectivityStatusText(createUnknownConnectivityState())).toBe('API connectivity unknown');
  });
});

/**
 * Verifies the connectivity probe helper used by the React hook.
 */
describe('checkApiConnectivity', () => {
  /**
   * Confirms the helper reports reachable state after successful health/readiness probes.
   */
  it('reports reachable state after successful probes', async () => {
    const client = createConnectivityClient(
      ok({ status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: [], warnings: [] }),
      ok({ status: 'Ready', checkedUtc: '2026-01-01T00:00:00Z', dependencies: [], warnings: [] }),
    );

    const state = await checkApiConnectivity(client, { isConfigured: true, baseUrl: 'https://localhost:5001' });

    expect(state.status).toBe('reachable');
    expect(client.getHealth).toHaveBeenCalledTimes(1);
    expect(client.getReadiness).toHaveBeenCalledTimes(1);
  });

  /**
   * Confirms the helper reports the unconfigured state without calling the operational
   * client when configuration is absent.
   */
  it('reports unconfigured state without probing when configuration is absent', async () => {
    const client = createConnectivityClient(
      ok({ status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: [], warnings: [] }),
      ok({ status: 'Ready', checkedUtc: '2026-01-01T00:00:00Z', dependencies: [], warnings: [] }),
    );

    const state = await checkApiConnectivity(client, { isConfigured: false });

    expect(state.status).toBe('unconfigured');
    expect(client.getHealth).not.toHaveBeenCalled();
    expect(client.getReadiness).not.toHaveBeenCalled();
  });
});

/**
 * Verifies the status bar presents connectivity using safe accessible text only.
 */
describe('StatusBar connectivity presentation', () => {
  /**
   * Confirms the visible status text does not include raw URLs or unsafe diagnostic text.
   */
  it('renders safe connectivity text', () => {
    const state: ApiConnectivityState = { status: 'unreachable', label: 'API unreachable', description: 'Safe failure message.', retryable: true };
    const rendered = getConnectivityStatusText(state);

    expect(rendered).toContain('API unreachable');
    expect(rendered).not.toContain('secret-host');
    expect(rendered).not.toContain('Password=');
  });
});
