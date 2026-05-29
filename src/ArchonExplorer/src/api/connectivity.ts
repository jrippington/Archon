import type { VariantProps } from 'class-variance-authority';
import type { ApiConfiguration } from '@/config/apiConfiguration';
import type { Badge } from '@/components/ui/badge';
import type { ManagementHealthResponse, ManagementReadinessResponse, NormalizedArchonApiError } from './archonApiTypes';
import type { ArchonApiRequestResult } from './request';

/**
 * Describes the minimal operational client surface needed for connectivity probes.
 */
export interface ApiConnectivityProbeClient {
  /**
   * Reads the API health endpoint with optional cancellation.
   *
   * @param controls - Optional cancellation controls from TanStack Query or a caller.
   * @returns A typed health result or a normalized safe failure.
   */
  getHealth(controls?: { readonly signal?: AbortSignal }): Promise<ArchonApiRequestResult<ManagementHealthResponse>>;

  /**
   * Reads the API readiness endpoint with optional cancellation.
   *
   * @param controls - Optional cancellation controls from TanStack Query or a caller.
   * @returns A typed readiness result or a normalized safe failure.
   */
  getReadiness(controls?: { readonly signal?: AbortSignal }): Promise<ArchonApiRequestResult<ManagementReadinessResponse>>;
}

/**
 * Names the machine-readable API connectivity states exposed to the workbench.
 */
export type ApiConnectivityStatus = 'configured' | 'unconfigured' | 'checking' | 'reachable' | 'notReady' | 'unreachable' | 'unknown';

/**
 * Describes the safe connectivity state consumed by shell components.
 */
export interface ApiConnectivityState {
  /**
   * Contains the machine-readable state used by tests, badges, and later diagnostics.
   */
  readonly status: ApiConnectivityStatus;

  /**
   * Contains short user-facing text that is safe to render in compact UI.
   */
  readonly label: string;

  /**
   * Contains optional explanatory text without raw URLs, secrets, stack traces, or backend diagnostics.
   */
  readonly description?: string;

  /**
   * Indicates whether a later UI may offer a manual recheck affordance.
   */
  readonly retryable: boolean;
}

/**
 * Executes health and readiness probes and derives safe connectivity state.
 *
 * @param client - The operational client that owns typed health and readiness calls.
 * @param apiConfiguration - The safe API configuration state used to short-circuit setup failures.
 * @param signal - Optional cancellation signal supplied by a query or caller.
 * @returns The derived safe connectivity state for the completed probe attempt.
 */
export async function checkApiConnectivity(client: ApiConnectivityProbeClient, apiConfiguration: ApiConfiguration, signal?: AbortSignal): Promise<ApiConnectivityState> {
  // Probing stops after health failure so the runtime can distinguish an unreachable
  // API from an API that responds locally but cannot satisfy readiness dependencies.
  if (!apiConfiguration.isConfigured) {
    return deriveConnectivityState({ apiConfiguration });
  }

  const health = await client.getHealth({ signal });
  if (!health.ok) {
    return deriveConnectivityState({ apiConfiguration, health });
  }

  const readiness = await client.getReadiness({ signal });
  return deriveConnectivityState({ apiConfiguration, health, readiness });
}

/**
 * Describes the inputs required to derive connectivity from configuration and probes.
 */
export interface ConnectivityDerivationInput {
  /**
   * Supplies the safe API base URL configuration state.
   */
  readonly apiConfiguration: ApiConfiguration;

  /**
   * Supplies the optional health check result when a probe has completed.
   */
  readonly health?: ArchonApiRequestResult<ManagementHealthResponse>;

  /**
   * Supplies the optional readiness check result when a probe has completed.
   */
  readonly readiness?: ArchonApiRequestResult<ManagementReadinessResponse>;

  /**
   * Indicates that the probe is currently in flight.
   */
  readonly checking?: boolean;
}

/**
 * Creates the initial configured-but-unchecked connectivity state.
 *
 * @returns A safe configured state that does not claim reachability.
 */
export function createConfiguredConnectivityState(): ApiConnectivityState {
  // Configuration proves only that the base URL exists; health and readiness still
  // need runtime probes before the shell can claim the API is reachable.
  return {
    status: 'configured',
    label: 'API configured; connectivity not checked',
    description: 'ArchonExplorer has an API base URL, but no connectivity probe has completed yet.',
    retryable: true,
  };
}

/**
 * Creates the unconfigured connectivity state.
 *
 * @returns A safe setup state for missing API base URL configuration.
 */
export function createUnconfiguredConnectivityState(): ApiConnectivityState {
  // The state intentionally omits the Vite environment key and any raw configuration
  // value so setup feedback remains safe for browser display.
  return {
    status: 'unconfigured',
    label: 'API base URL not configured',
    description: 'Set the Archon API base URL before API-backed features can run.',
    retryable: false,
  };
}

/**
 * Creates the checking connectivity state.
 *
 * @returns A safe in-flight probe state.
 */
export function createCheckingConnectivityState(): ApiConnectivityState {
  // The shell uses this state while TanStack Query runs health and readiness checks.
  return {
    status: 'checking',
    label: 'Checking API connectivity',
    description: 'ArchonExplorer is checking the API health and readiness endpoints.',
    retryable: false,
  };
}

/**
 * Creates the reachable connectivity state.
 *
 * @returns A safe state indicating that health and readiness both succeeded.
 */
export function createReachableConnectivityState(): ApiConnectivityState {
  // Reachable is intentionally based on both probes so the shell does not treat an
  // alive-but-not-ready API as ready for feature work.
  return {
    status: 'reachable',
    label: 'API reachable and ready',
    description: 'ArchonApi health and readiness checks completed successfully.',
    retryable: true,
  };
}

/**
 * Creates the not-ready connectivity state.
 *
 * @returns A safe state indicating that health succeeded but readiness did not.
 */
export function createNotReadyConnectivityState(): ApiConnectivityState {
  // The description avoids naming failed dependencies because readiness details may
  // include operational context that belongs in controlled diagnostics, not the shell.
  return {
    status: 'notReady',
    label: 'API reachable; dependencies not ready',
    description: 'ArchonApi responded to health checks, but readiness is not available yet.',
    retryable: true,
  };
}

/**
 * Creates the unreachable connectivity state.
 *
 * @param error - Optional normalized error used only for retryability, never for raw text display.
 * @returns A safe state indicating that the API could not be reached.
 */
export function createUnreachableConnectivityState(error?: NormalizedArchonApiError): ApiConnectivityState {
  // The label and description are controlled strings; the normalized error contributes
  // retryability only so raw backend or browser details never appear in the status bar.
  return {
    status: 'unreachable',
    label: 'API unreachable',
    description: 'ArchonApi could not be reached from the browser.',
    retryable: error?.retryable ?? true,
  };
}

/**
 * Creates the unknown connectivity state.
 *
 * @param error - Optional normalized error used only for retryability, never for raw text display.
 * @returns A safe state for unexpected or inconclusive probe outcomes.
 */
export function createUnknownConnectivityState(error?: NormalizedArchonApiError): ApiConnectivityState {
  // Unexpected response shapes are intentionally grouped as unknown so the shell stays
  // honest without presenting response bodies, stack traces, or proxy diagnostics.
  return {
    status: 'unknown',
    label: 'API connectivity unknown',
    description: 'ArchonExplorer could not determine API connectivity from the safe probe results.',
    retryable: error?.retryable ?? true,
  };
}

/**
 * Derives a safe connectivity state from configuration and optional probe results.
 *
 * @param input - The safe configuration state and any completed health/readiness probe results.
 * @returns A machine-readable and user-facing connectivity state.
 */
export function deriveConnectivityState(input: ConnectivityDerivationInput): ApiConnectivityState {
  // Derivation is intentionally conservative: configuration absence wins, active probes
  // show checking, successful health requires successful readiness before reachability,
  // and unsafe or surprising failures collapse into controlled safe states.
  if (!input.apiConfiguration.isConfigured) {
    return createUnconfiguredConnectivityState();
  }

  if (input.checking === true) {
    return createCheckingConnectivityState();
  }

  if (input.health === undefined) {
    return createConfiguredConnectivityState();
  }

  if (!input.health.ok) {
    return mapConnectivityError(input.health.error);
  }

  if (input.readiness === undefined) {
    return createCheckingConnectivityState();
  }

  if (input.readiness.ok) {
    return createReachableConnectivityState();
  }

  if (input.readiness.error.category === 'network' || input.readiness.error.category === 'timeout') {
    return createUnreachableConnectivityState(input.readiness.error);
  }

  if (input.readiness.error.category === 'server' || input.readiness.error.category === 'conflict' || input.readiness.error.category === 'notFound') {
    return createNotReadyConnectivityState();
  }

  return createUnknownConnectivityState(input.readiness.error);
}

/**
 * Converts a normalized probe failure into a safe connectivity state.
 *
 * @param error - The normalized failure returned by the request foundation.
 * @returns A safe connectivity state that does not expose raw failure text.
 */
function mapConnectivityError(error: NormalizedArchonApiError): ApiConnectivityState {
  // Configuration remains distinct because a missing base URL is an actionable setup
  // state, while network and timeout indicate the configured endpoint could not be reached.
  if (error.category === 'configuration') {
    return createUnconfiguredConnectivityState();
  }

  if (error.category === 'network' || error.category === 'timeout') {
    return createUnreachableConnectivityState(error);
  }

  return createUnknownConnectivityState(error);
}

/**
 * Gets compact status text for a connectivity state.
 *
 * @param state - The connectivity state to present.
 * @returns The safe compact label for display.
 */
export function getConnectivityStatusText(state: ApiConnectivityState): string {
  // Keeping the display text in the state model makes component rendering simple and
  // ensures tests can assert the safe wording independent of React.
  return state.label;
}

/**
 * Gets the badge variant appropriate for a connectivity state.
 *
 * @param state - The connectivity state to visualize.
 * @returns A local Badge variant name for compact status rendering.
 */
export function getConnectivityBadgeVariant(state: ApiConnectivityState): VariantProps<typeof Badge>['variant'] {
  // The visual treatment is secondary to the text: every state still renders a label
  // so color is not the only accessibility signal.
  if (state.status === 'reachable') {
    return 'secondary';
  }

  if (state.status === 'configured' || state.status === 'checking') {
    return 'outline';
  }

  return 'warning';
}
