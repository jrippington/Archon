import { Badge } from '@/components/ui/badge';
import type { ApiConfiguration } from '@/config/apiConfiguration';

/**
 * Describes the status bar inputs supplied by the application shell.
 */
export interface StatusBarProps {
  /**
   * Safe API configuration state read from the Vite environment adapter.
   */
  readonly apiConfiguration: ApiConfiguration;
}

/**
 * Converts API configuration state into safe user-facing status text.
 *
 * @param apiConfiguration The safe frontend configuration object read by the app root.
 * @returns A short status label that does not expose raw environment variables or secrets.
 */
function getApiStatusText(apiConfiguration: ApiConfiguration): string {
  // The shell reports only whether an API base URL exists; connectivity, exact endpoints,
  // environment variable names, and diagnostics belong to later controlled workflows.
  return apiConfiguration.isConfigured ? 'API configuration present' : 'API configuration not set';
}

/**
 * Renders the bottom status bar for reserved workbench context.
 *
 * @param props Contains safe runtime status values for the current shell render.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @returns A status landmark with snapshot, API, background work, and selection placeholders.
 */
export function StatusBar({ apiConfiguration }: StatusBarProps) {
  // Each item includes explicit text so status is not conveyed by color or badge treatment alone.
  return (
    <footer className="workbench-status-bar" aria-label="ArchonExplorer shell status">
      <span>
        Active snapshot: <strong>current unavailable</strong>
      </span>
      <span>
        <Badge variant={apiConfiguration.isConfigured ? 'secondary' : 'warning'}>
          {getApiStatusText(apiConfiguration)}
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
