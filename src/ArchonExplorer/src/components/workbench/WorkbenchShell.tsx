import { Badge } from '@/components/ui/badge';
import { useMemo, useRef } from 'react';
import type React from 'react';
import type { ApiConnectivityState } from '@/api/connectivity';
import type { ApiConfiguration } from '@/config/apiConfiguration';
import { Button } from '@/components/ui/button';
import { maximumBottomPanelHeightPercent, maximumSidebarWidthPercent, minimumBottomPanelHeightPercent, minimumSidebarWidthPercent } from '@/lib/workbenchPersistence';
import { ActivityRail } from './ActivityRail';
import { BottomPanel } from './BottomPanel';
import { CommandPalette } from './CommandPalette';
import { NotificationHost } from './NotificationHost';
import { PrimarySidebar } from './PrimarySidebar';
import { StatusBar } from './StatusBar';
import { TabbedWorkArea } from './TabbedWorkArea';
import { ThemeToggle } from './ThemeToggle';
import { getWorkbenchShellCommands } from './workbenchCommands';
import { useWorkbenchKeyboardShortcuts } from '@/hooks/useWorkbenchKeyboardShortcuts';
import { useNotifications } from '@/providers/NotificationProvider';
import { useWorkbenchStore, WorkbenchStoreProvider } from '@/state/workbenchStore';

/**
 * Describes the runtime inputs needed by the visible workbench shell.
 */
export interface WorkbenchShellProps {
  /**
   * Safe API configuration state shown in the header and status bar.
   */
  readonly apiConfiguration: ApiConfiguration;

  /**
   * Optional safe connectivity override used by deterministic tests.
   */
  readonly connectivityState?: ApiConnectivityState;
}

/**
 * Renders the top-level ArchonExplorer workbench frame.
 *
 * @param props Contains safe runtime status values for the shell.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @param props.connectivityState Optional safe connectivity state override for tests.
 * @returns The desktop-style shell frame with placeholders for future workbench regions.
 */
export function WorkbenchShell({ apiConfiguration, connectivityState }: WorkbenchShellProps) {
  // The provider keeps Work Item 1 state local to the shell while preserving the existing
  // application-level query and notification providers from the runtime foundation.
  return (
    <WorkbenchStoreProvider>
      <WorkbenchShellFrame apiConfiguration={apiConfiguration} connectivityState={connectivityState} />
    </WorkbenchStoreProvider>
  );
}

/**
 * Renders the state-consuming workbench shell frame.
 *
 * @param props Contains safe runtime status values for the shell.
 * @param props.apiConfiguration The safe API base URL configuration state.
 * @param props.connectivityState Optional safe connectivity state override for tests.
 * @returns The desktop-style shell frame bound to local workbench state.
 */
function WorkbenchShellFrame({ apiConfiguration, connectivityState }: WorkbenchShellProps) {
  // The shell composes stable regions only; feature behavior remains absent so later work
  // packages can add real queries, commands, and panels behind these state-driven seams.
  const {
    state,
    selectActivity,
    selectTab,
    toggleBottomPanel,
    showBottomPanel,
    hideBottomPanel,
    setSidebarWidthPercent,
    setBottomPanelHeightPercent,
    resetLayoutPreferences,
    setCommandPaletteVisible,
  } = useWorkbenchStore();
  const { notifyInformation } = useNotifications();
  const commandPaletteTriggerRef = useRef<HTMLButtonElement | null>(null);
  const commandPaletteCommands = useMemo(() => getWorkbenchShellCommands({
    state,
    selectActivity,
    selectTab,
    toggleBottomPanel,
    showBottomPanel,
    hideBottomPanel,
    resetLayoutPreferences,
    setCommandPaletteVisible,
    notifyInformation,
  }), [hideBottomPanel, notifyInformation, resetLayoutPreferences, selectActivity, selectTab, setCommandPaletteVisible, showBottomPanel, state, toggleBottomPanel]);
  useWorkbenchKeyboardShortcuts({
    isCommandPaletteVisible: state.panels.isCommandPaletteVisible,
    setCommandPaletteVisible,
    triggerRef: commandPaletteTriggerRef,
  });
  const shellContentStyle = {
    '--workbench-sidebar-width': state.panels.isSidebarCollapsed
      ? '0rem'
      : `${state.panels.sidebarWidthPercent}%`,
    '--workbench-bottom-panel-height': `${state.panels.bottomPanelHeightPercent}%`,
  } as React.CSSProperties;
  const resizeSidebar = createResizeHandler({
    currentValue: state.panels.sidebarWidthPercent,
    minimumValue: minimumSidebarWidthPercent,
    maximumValue: maximumSidebarWidthPercent,
    step: 2,
    onResize: setSidebarWidthPercent,
  });
  const resizeBottomPanel = createResizeHandler({
    currentValue: state.panels.bottomPanelHeightPercent,
    minimumValue: minimumBottomPanelHeightPercent,
    maximumValue: maximumBottomPanelHeightPercent,
    step: 2,
    onResize: setBottomPanelHeightPercent,
  });

  return (
    <div className="workbench-shell">
      <ActivityRail activeActivityId={state.activeActivityId} onSelectActivity={selectActivity} />
      <div className="workbench-shell__main">
        <header className="workbench-top-bar">
          <div className="workbench-top-bar__title-group">
            <Badge variant="outline">ArchonExplorer</Badge>
            <div>
              <p className="workbench-top-bar__eyebrow">Architecture intelligence workbench</p>
              <p className="workbench-top-bar__title">Foundation shell</p>
            </div>
          </div>
          <CommandPaletteTrigger onOpen={() => setCommandPaletteVisible(true)} triggerRef={commandPaletteTriggerRef} />
          <div className="workbench-top-bar__actions" aria-label="Shell controls and setup state">
            <Badge variant={apiConfiguration.isConfigured ? 'secondary' : 'warning'}>
              {apiConfiguration.isConfigured ? 'API configured' : 'API not configured'}
            </Badge>
            <Button type="button" variant="outline" size="sm" onClick={state.panels.isBottomPanelVisible ? hideBottomPanel : showBottomPanel}>
              {state.panels.isBottomPanelVisible ? 'Hide bottom panel' : 'Show bottom panel'}
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={resetLayoutPreferences}>
              Reset layout
            </Button>
            <ThemeToggle />
          </div>
        </header>
        <div className="workbench-shell__content" style={shellContentStyle}>
          <div className="workbench-shell__horizontal-region">
            <PrimarySidebar activeActivityId={state.activeActivityId} />
            <button
              type="button"
              className="workbench-resize-handle workbench-resize-handle--vertical"
              aria-label="Resize primary sidebar"
              aria-orientation="vertical"
              aria-valuemin={minimumSidebarWidthPercent}
              aria-valuemax={maximumSidebarWidthPercent}
              aria-valuenow={state.panels.sidebarWidthPercent}
              disabled={state.panels.isSidebarCollapsed}
              onKeyDown={resizeSidebar}
              role="separator"
            />
            <section className="workbench-shell__work-region" aria-label="Workbench editor and bottom panel region">
              <TabbedWorkArea tabs={state.openTabs} activeTabId={state.activeTabId} onSelectTab={selectTab} />
              {state.panels.isBottomPanelVisible ? (
                <>
                  <button
                    type="button"
                    className="workbench-resize-handle workbench-resize-handle--horizontal"
                    aria-label="Resize bottom panel"
                    aria-orientation="horizontal"
                    aria-valuemin={minimumBottomPanelHeightPercent}
                    aria-valuemax={maximumBottomPanelHeightPercent}
                    aria-valuenow={state.panels.bottomPanelHeightPercent}
                    onKeyDown={resizeBottomPanel}
                    role="separator"
                  />
                  <BottomPanel onHide={hideBottomPanel} />
                </>
              ) : null}
            </section>
          </div>
        </div>
        <NotificationHost />
        <StatusBar apiConfiguration={apiConfiguration} connectivityState={connectivityState} isBottomPanelVisible={state.panels.isBottomPanelVisible} activeActivityId={state.activeActivityId} />
      </div>
      <CommandPalette
        commands={commandPaletteCommands}
        isOpen={state.panels.isCommandPaletteVisible}
        onClose={() => setCommandPaletteVisible(false)}
      />
    </div>
  );
}

/**
 * Describes the visible command-palette trigger inputs.
 */
interface CommandPaletteTriggerProps {
  /**
   * Opens the command palette when the user activates the affordance.
   */
  readonly onOpen: () => void;

  /**
   * References the trigger so focus can return after palette dismissal.
   */
  readonly triggerRef: React.RefObject<HTMLButtonElement | null>;
}

/**
 * Renders the header affordance that opens the shell command palette.
 *
 * @param props Contains the open callback and trigger ref used for focus restoration.
 * @param props.onOpen Callback invoked when the user opens the command palette.
 * @param props.triggerRef Ref attached to the trigger button for focus restoration.
 * @returns A command/search affordance that makes future architecture search boundaries explicit.
 */
function CommandPaletteTrigger({ onOpen, triggerRef }: CommandPaletteTriggerProps) {
  // The trigger replaces the earlier disabled placeholder: commands are now functional, while the
  // copy still clearly states that global architecture search remains a later work package.
  return (
    <div className="workbench-command-search" aria-label="Command palette and future search">
      <div className="workbench-command-search__copy">
        <span>Run shell commands</span>
        <span>Global architecture search arrives in a later work package.</span>
      </div>
      <Badge variant="outline">Ctrl+K</Badge>
      <Button ref={triggerRef} aria-keyshortcuts="Control+K Meta+K" type="button" variant="outline" onClick={onOpen}>
        Open command palette
      </Button>
    </div>
  );
}

/**
 * Describes the inputs needed to create a keyboard resize handler for shell separators.
 */
interface ResizeHandlerOptions {
  /**
   * Stores the current percentage value before keyboard input is applied.
   */
  readonly currentValue: number;

  /**
   * Defines the smallest accepted percentage value for the target region.
   */
  readonly minimumValue: number;

  /**
   * Defines the largest accepted percentage value for the target region.
   */
  readonly maximumValue: number;

  /**
   * Defines the percentage increment used for arrow-key resizing.
   */
  readonly step: number;

  /**
   * Applies the normalized percentage value to local workbench state.
   */
  readonly onResize: (value: number) => void;
}

/**
 * Creates an accessible keyboard handler for local resizable shell separators.
 *
 * @param options Contains current value, bounds, step size, and update callback for one separator.
 * @returns A React keyboard event handler that adjusts the region size with arrow, Home, and End keys.
 */
function createResizeHandler(options: ResizeHandlerOptions): React.KeyboardEventHandler<HTMLButtonElement> {
  // The shell uses a minimal in-repository separator rather than a heavy layout dependency. The
  // handler gives keyboard users deterministic control while pointer-drag behavior can be added
  // later behind the same persisted percentage state.
  return (event) => {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      options.onResize(options.currentValue - options.step);
      return;
    }

    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      options.onResize(options.currentValue + options.step);
      return;
    }

    if (event.key === 'Home') {
      event.preventDefault();
      options.onResize(options.minimumValue);
      return;
    }

    if (event.key === 'End') {
      event.preventDefault();
      options.onResize(options.maximumValue);
    }
  };
}
