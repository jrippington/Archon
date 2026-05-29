import { Badge } from '@/components/ui/badge';
import type { ApiConnectivityState } from '@/api/connectivity';
import { getConnectivityBadgeVariant, getConnectivityStatusText } from '@/api/connectivity';
import type { ApiConfiguration } from '@/config/apiConfiguration';
import { useApiConnectivity } from '@/hooks/useApiConnectivity';
import { getWorkbenchActivity, type WorkbenchActivityId } from './workbenchActivities';

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

  /**
   * Indicates whether the local bottom panel region is currently visible.
   */
  readonly isBottomPanelVisible?: boolean;

  /**
   * Identifies the active local workbench activity for selected-context status text.
   */
  readonly activeActivityId?: WorkbenchActivityId;
}

/**
 * Renders the bottom status bar for reserved workbench context.
 *
 * @param props Contains safe runtime status values for the current shell render.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @param props.connectivityState Optional safe connectivity state override for deterministic tests.
 * @returns A status landmark with snapshot, API, background work, and selection placeholders.
 */
export function StatusBar({ apiConfiguration, connectivityState, isBottomPanelVisible = false, activeActivityId = 'dashboard' }: StatusBarProps) {
  // The wrapper delegates to a hook-consuming child only when a deterministic override is not
  // supplied. This keeps tests and controlled hosts from needing a QueryClient just to render
  // static status text, while production still uses the WP002 connectivity hook.
  if (connectivityState !== undefined) {
    return <StatusBarContent connectivityState={connectivityState} isBottomPanelVisible={isBottomPanelVisible} activeActivityId={activeActivityId} />;
  }

  return <ProbedStatusBarContent apiConfiguration={apiConfiguration} isBottomPanelVisible={isBottomPanelVisible} activeActivityId={activeActivityId} />;
}

/**
 * Describes the hook-consuming status content inputs.
 */
interface ProbedStatusBarContentProps {
  /**
   * Safe API configuration state used to derive connectivity through the shared hook.
   */
  readonly apiConfiguration: ApiConfiguration;

  /**
   * Indicates whether the local bottom panel region is currently visible.
   */
  readonly isBottomPanelVisible: boolean;

  /**
   * Identifies the active local workbench activity for selected-context status text.
   */
  readonly activeActivityId: WorkbenchActivityId;
}

/**
 * Renders status bar content after probing safe API connectivity through WP002 runtime seams.
 *
 * @param props Contains the safe API configuration used by the connectivity hook.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @param props.activeActivityId The selected local activity used for status text.
 * @param props.isBottomPanelVisible Indicates whether the bottom-panel region is currently visible.
 * @returns The pure status bar content with hook-derived connectivity state.
 */
function ProbedStatusBarContent({ apiConfiguration, isBottomPanelVisible, activeActivityId }: ProbedStatusBarContentProps) {
  // The connectivity hook must remain in this child component so StatusBar can short-circuit
  // controlled render paths without violating React's rules of hooks.
  const safeConnectivityState = useApiConnectivity({ apiConfiguration });

  return <StatusBarContent connectivityState={safeConnectivityState} isBottomPanelVisible={isBottomPanelVisible} activeActivityId={activeActivityId} />;
}

/**
 * Describes the pure status bar presentation inputs.
 */
export interface StatusBarContentProps {
  /**
   * Safe connectivity state already derived by the hook or a deterministic test setup.
   */
  readonly connectivityState: ApiConnectivityState;

  /**
   * Indicates whether bottom-panel placeholder state should be reported as visible or hidden.
   */
  readonly isBottomPanelVisible?: boolean;

  /**
   * Identifies the selected local activity used for safe context text.
   */
  readonly activeActivityId?: WorkbenchActivityId;
}

/**
 * Renders the status bar once safe connectivity state has been derived.
 *
 * @param props Contains the safe connectivity state for presentation.
 * @param props.activeActivityId The selected local activity used for status text.
 * @param props.connectivityState The safe connectivity state shown in compact status text.
 * @param props.isBottomPanelVisible Indicates whether the bottom-panel region is currently visible.
 * @returns A status landmark with snapshot, API, background work, and selection placeholders.
 */
export function StatusBarContent({ connectivityState, isBottomPanelVisible = false, activeActivityId = 'dashboard' }: StatusBarContentProps) {
  // This pure component keeps status text rendering testable without executing network
  // probes, while StatusBar remains the production hook-consuming wrapper.
  const safeConnectivityState = connectivityState;
  const activeActivity = getWorkbenchActivity(activeActivityId);

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
        Background work: <strong>none running; bottom panel {isBottomPanelVisible ? 'visible' : 'hidden'}</strong>
      </span>
      <span>
        Selection: <strong>{activeActivity.label} activity selected; no item selected</strong>
      </span>
    </footer>
  );
}
