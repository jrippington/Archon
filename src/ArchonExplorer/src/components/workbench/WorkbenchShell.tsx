import { Badge } from '@/components/ui/badge';
import type { ApiConfiguration } from '@/config/apiConfiguration';
import { ActivityRail } from './ActivityRail';
import { CommandSearchPlaceholder } from './CommandSearchPlaceholder';
import { StatusBar } from './StatusBar';
import { ThemeToggle } from './ThemeToggle';
import { WorkspaceStartState } from './WorkspaceStartState';

/**
 * Describes the runtime inputs needed by the visible workbench shell.
 */
export interface WorkbenchShellProps {
  /**
   * Safe API configuration state shown in the header and status bar.
   */
  readonly apiConfiguration: ApiConfiguration;
}

/**
 * Renders the top-level ArchonExplorer workbench frame.
 *
 * @param props Contains safe runtime status values for the shell.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @returns The desktop-style shell frame with placeholders for future workbench regions.
 */
export function WorkbenchShell({ apiConfiguration }: WorkbenchShellProps) {
  // The shell composes visible regions only; feature behavior remains absent so later work
  // packages can add real routes, queries, and commands behind these stable seams.
  return (
    <div className="workbench-shell">
      <ActivityRail />
      <div className="workbench-shell__main">
        <header className="workbench-top-bar">
          <div className="workbench-top-bar__title-group">
            <Badge variant="outline">ArchonExplorer</Badge>
            <div>
              <p className="workbench-top-bar__eyebrow">Architecture intelligence workbench</p>
              <p className="workbench-top-bar__title">Foundation shell</p>
            </div>
          </div>
          <CommandSearchPlaceholder />
          <div className="workbench-top-bar__actions" aria-label="Shell controls and setup state">
            <Badge variant={apiConfiguration.isConfigured ? 'secondary' : 'warning'}>
              {apiConfiguration.isConfigured ? 'API configured' : 'API not configured'}
            </Badge>
            <ThemeToggle />
          </div>
        </header>
        <WorkspaceStartState />
        <StatusBar apiConfiguration={apiConfiguration} />
      </div>
    </div>
  );
}
