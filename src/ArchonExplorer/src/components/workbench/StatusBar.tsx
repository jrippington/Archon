import { Badge } from '@/components/ui/badge';
import type { ApiConnectivityState } from '@/api/connectivity';
import { getConnectivityBadgeVariant, getConnectivityStatusText } from '@/api/connectivity';
import type { ApiConfiguration } from '@/config/apiConfiguration';
import { useApiConnectivity } from '@/hooks/useApiConnectivity';

/**
 * Describes the status bar inputs supplied by the application shell.
 */
export interface StatusBarProps {
  /**
   * Safe API configuration state read from the Vite environment adapter.
   */
  readonly apiConfiguration: ApiConfiguration;

  /**
 * Optional safe connectivity state override used by controlled host scenarios.
   */
  readonly connectivityState?: ApiConnectivityState;
}

/**
 * Renders the bottom status bar for reserved workbench context.
 *
 * @param props Contains safe runtime status values for the current shell render.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @param props.connectivityState Optional safe connectivity state override for deterministic tests.
 * @returns A status landmark with snapshot, API, background work, and selection placeholders.
 */
export function StatusBar({ apiConfiguration, connectivityState }: StatusBarProps) {
  // The status bar consumes the connectivity state only as safe text. It does not
  // display raw URLs, response bodies, dependency names, stack traces, or retry controls.
  const probedConnectivityState = useApiConnectivity({ apiConfiguration });
  const safeConnectivityState = connectivityState ?? probedConnectivityState;

  return <StatusBarContent connectivityState={safeConnectivityState} />;
}

/**
 * Describes the pure status bar presentation inputs.
 */
export interface StatusBarContentProps {
  /**
   * Safe connectivity state already derived by the hook or a deterministic test setup.
   */
  readonly connectivityState: ApiConnectivityState;
}

/**
 * Renders the status bar once safe connectivity state has been derived.
 *
 * @param props Contains the safe connectivity state for presentation.
 * @param props.connectivityState The safe connectivity state shown in compact status text.
 * @returns A status landmark with snapshot, API, background work, and selection placeholders.
 */
export function StatusBarContent({ connectivityState }: StatusBarContentProps) {
  // This pure component keeps status text rendering testable without executing network
  // probes, while StatusBar remains the production hook-consuming wrapper.
  const safeConnectivityState = connectivityState;

  // Each item includes explicit text so status is not conveyed by color or badge treatment alone.
  return (
    <footer className="workbench-status-bar" aria-label="ArchonExplorer shell status">
      <span>
        Active snapshot: <strong>current unavailable</strong>
      </span>
      <span>
        <Badge variant={getConnectivityBadgeVariant(safeConnectivityState)}>
          {getConnectivityStatusText(safeConnectivityState)}
        </Badge>
      </span>
      <span>
        Background work: <strong>none running</strong>
      </span>
      <span>
        Selection: <strong>nothing selected</strong>
      </span>
    </footer>
  );
}
