import { defaultWorkbenchTabId, type WorkbenchState } from '@/state/workbenchStore';
import { workbenchActivities, type WorkbenchActivityId } from './workbenchActivities';
import type { OperationNotificationOptions } from '@/providers/NotificationProvider';

/**
 * Names the stable command groups shown in the Workbench command palette.
 */
export type WorkbenchCommandGroup = 'Activities' | 'Panels' | 'Tabs' | 'Layout' | 'Focus' | 'Future Search';

/**
 * Describes one shell-level command that can be rendered and executed by the command palette.
 */
export interface WorkbenchShellCommand {
  /**
   * Stores the stable command identity used by tests, command execution, and future extension points.
   */
  readonly id: string;

  /**
   * Provides the human-readable command label shown in the palette.
   */
  readonly label: string;

  /**
   * Names the visible group that organizes this command in the palette.
   */
  readonly group: WorkbenchCommandGroup;

  /**
   * Provides optional supporting text that clarifies command boundaries or side effects.
   */
  readonly description?: string;

  /**
   * Shows an optional keyboard hint without registering another global shortcut.
   */
  readonly keyboardHint?: string;

  /**
   * Indicates that the command is visible but not currently available for normal execution.
   */
  readonly isDisabled?: boolean;

  /**
   * Explains why a disabled command is present in the shell.
   */
  readonly disabledReason?: string;

  /**
   * Executes the command against browser-local workbench state or safe notification feedback.
   */
  readonly execute: () => void;
}

/**
 * Describes the shell state and actions required when building command registrations.
 */
export interface WorkbenchCommandContext {
  /**
   * Provides the current browser-local shell state snapshot used for labels and disabled states.
   */
  readonly state: WorkbenchState;

  /**
   * Selects an activity inside the local workbench frame.
   */
  readonly selectActivity: (activityId: string) => void;

  /**
   * Selects an existing tab inside the local work area.
   */
  readonly selectTab: (tabId: string) => void;

  /**
   * Toggles the bottom panel without loading feature data.
   */
  readonly toggleBottomPanel: () => void;

  /**
   * Shows the bottom panel region for focus-oriented commands.
   */
  readonly showBottomPanel: () => void;

  /**
   * Hides the bottom panel region for focus-oriented commands.
   */
  readonly hideBottomPanel: () => void;

  /**
   * Resets browser-local layout preferences to documented defaults.
   */
  readonly resetLayoutPreferences: () => void;

  /**
   * Sets command palette visibility after commands complete.
   */
  readonly setCommandPaletteVisible: (isVisible: boolean) => void;

  /**
   * Publishes safe transient shell feedback through the notification runtime.
   */
  readonly notifyInformation: (options: OperationNotificationOptions) => void;
}

/**
 * Creates command registrations for the current Workbench shell state.
 *
 * @param context The local shell state, state actions, and safe notification helper used by commands.
 * @returns A grouped command list for the Workbench command palette.
 */
export function getWorkbenchShellCommands(context: WorkbenchCommandContext): readonly WorkbenchShellCommand[] {
  // Commands intentionally execute against local state callbacks rather than browser navigation.
  // Later feature packages can add command providers behind this registry shape without changing
  // the palette's rendering or keyboard model.
  return [
    ...createActivityCommands(context),
    ...createPanelCommands(context),
    ...createTabCommands(context),
    ...createLayoutCommands(context),
    ...createFocusCommands(context),
    createFutureSearchCommand(context),
  ];
}

/**
 * Creates one activity-switching command for each registered workbench activity.
 *
 * @param context The command context that supplies the activity selection action.
 * @returns Activity commands that keep users inside the desktop shell frame.
 */
function createActivityCommands(context: WorkbenchCommandContext): readonly WorkbenchShellCommand[] {
  // Activity commands are generated from the activity catalog so the rail, sidebar, status bar,
  // and command palette cannot drift into different placeholder activity sets.
  return workbenchActivities.map((activity) => ({
    id: `workbench.activity.${activity.id}`,
    label: `Switch to ${activity.label}`,
    group: 'Activities',
    description: activity.description,
    execute: () => {
      context.selectActivity(activity.id);
    },
  } satisfies WorkbenchShellCommand));
}

/**
 * Creates commands for the bottom panel shell region.
 *
 * @param context The command context that supplies panel visibility state and actions.
 * @returns Panel commands for toggling or explicitly showing and hiding the bottom panel.
 */
function createPanelCommands(context: WorkbenchCommandContext): readonly WorkbenchShellCommand[] {
  // Explicit show/hide commands complement the toggle command so keyboard users can recover to a
  // known state without inspecting the current visible shell layout first.
  return [
    {
      id: 'workbench.panel.bottom.toggle',
      label: 'Toggle Bottom Panel',
      group: 'Panels',
      description: 'Show or hide the safe bottom panel placeholder region.',
      execute: context.toggleBottomPanel,
    },
    {
      id: 'workbench.panel.bottom.show',
      label: 'Show Bottom Panel',
      group: 'Panels',
      description: 'Open the bottom panel without loading extraction runs or diagnostics.',
      isDisabled: context.state.panels.isBottomPanelVisible,
      disabledReason: context.state.panels.isBottomPanelVisible ? 'The bottom panel is already visible.' : undefined,
      execute: context.showBottomPanel,
    },
    {
      id: 'workbench.panel.bottom.hide',
      label: 'Hide Bottom Panel',
      group: 'Panels',
      description: 'Hide the bottom panel while preserving its local layout preference.',
      isDisabled: !context.state.panels.isBottomPanelVisible,
      disabledReason: !context.state.panels.isBottomPanelVisible ? 'The bottom panel is already hidden.' : undefined,
      execute: context.hideBottomPanel,
    },
  ];
}

/**
 * Creates commands for stable workbench tab behavior.
 *
 * @param context The command context that supplies tab selection.
 * @returns Tab commands for focusing the required start tab.
 */
function createTabCommands(context: WorkbenchCommandContext): readonly WorkbenchShellCommand[] {
  // The shell currently has one required start tab. Using a command now proves the tab command
  // seam without creating fabricated document tabs or feature-specific editor content.
  return [
    {
      id: 'workbench.tab.start',
      label: 'Open Workbench Start',
      group: 'Tabs',
      description: 'Focus the required local start tab in the work area.',
      execute: () => {
        context.selectTab(defaultWorkbenchTabId);
      },
    },
  ];
}

/**
 * Creates layout commands that affect safe browser-local preferences only.
 *
 * @param context The command context that supplies layout reset and notification actions.
 * @returns Layout commands for preference recovery.
 */
function createLayoutCommands(context: WorkbenchCommandContext): readonly WorkbenchShellCommand[] {
  // Layout reset is paired with a transient notification because the visible layout may reset
  // without a durable error or page-level message requirement.
  return [
    {
      id: 'workbench.layout.reset',
      label: 'Reset Layout Preferences',
      group: 'Layout',
      description: 'Restore local panel sizes, activity preference, and bottom-panel visibility to defaults.',
      execute: () => {
        context.resetLayoutPreferences();
        context.notifyInformation({
          operationName: 'Layout preferences reset',
          detail: 'The workbench shell restored default local layout preferences. No API configuration, diagnostics, or feature data was changed.',
        });
      },
    },
  ];
}

/**
 * Creates focus-oriented commands for major shell regions.
 *
 * @param context The command context that supplies local activity and panel actions.
 * @returns Focus commands that map to practical shell state changes where direct DOM focus is not required.
 */
function createFocusCommands(context: WorkbenchCommandContext): readonly WorkbenchShellCommand[] {
  // These commands route users to major regions through state changes that remain testable and
  // safe in server-rendered tests; direct DOM focus is handled by the palette hook and component.
  return [
    {
      id: 'workbench.focus.activityRail',
      label: 'Focus Activity Rail',
      group: 'Focus',
      description: 'Return to the active activity context in the shell frame.',
      keyboardHint: 'Activity rail',
      execute: () => {
        context.selectActivity(context.state.activeActivityId as WorkbenchActivityId);
      },
    },
    {
      id: 'workbench.focus.bottomPanel',
      label: 'Focus Bottom Panel',
      group: 'Focus',
      description: 'Open the bottom panel so keyboard focus can move to its controls.',
      execute: context.showBottomPanel,
    },
  ];
}

/**
 * Creates the disabled command that documents the future global search boundary.
 *
 * @param context The command context that supplies safe notification feedback.
 * @returns A disabled search command that never returns fabricated architecture results.
 */
function createFutureSearchCommand(context: WorkbenchCommandContext): WorkbenchShellCommand {
  // The command is intentionally present so users learn where global search will live, but its
  // execution only publishes safe feedback and never constructs fake search result state.
  return {
    id: 'workbench.search.future',
    label: 'Search Architecture Knowledge',
    group: 'Future Search',
    description: 'Global architecture search arrives in a later work package.',
    keyboardHint: 'Future',
    isDisabled: true,
    disabledReason: 'Architecture search is not implemented in this work package.',
    execute: () => {
      context.notifyInformation({
        operationName: 'Global architecture search is not available yet',
        detail: 'Architecture search arrives in a later work package. No search results, graph data, or architecture artefacts were loaded.',
      });
    },
  };
}
