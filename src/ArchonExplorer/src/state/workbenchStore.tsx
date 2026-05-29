import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, type ReactNode } from 'react';
import { getDefaultWorkbenchActivityId, isWorkbenchActivityId, type WorkbenchActivityId } from '@/components/workbench/workbenchActivities';
import {
  defaultWorkbenchPreferences,
  loadWorkbenchPreferences,
  maximumBottomPanelHeightPercent,
  maximumSidebarWidthPercent,
  minimumBottomPanelHeightPercent,
  minimumSidebarWidthPercent,
  normalizePercentPreference,
  resetWorkbenchPreferences,
  saveWorkbenchPreferences,
  type WorkbenchPreferences,
} from '@/lib/workbenchPersistence';

/**
 * Identifies a stable workbench tab within the browser-local shell state.
 */
export type WorkbenchTabId = 'workbench-start' | string;

/**
 * Names the stable workbench tab that hosts the Snapshot workspace slice.
 */
export const snapshotWorkspaceTabId: WorkbenchTabId = 'snapshot-workspace';

/**
 * Provides the tab descriptor used whenever the Snapshot workspace activity opens.
 */
export const snapshotWorkspaceTab: WorkbenchTab = {
  id: snapshotWorkspaceTabId,
  title: 'Snapshot Workspace',
  activityId: 'snapshots',
  isClosable: false,
  placeholderSummary: 'Snapshot workspace hosts explicit extraction requests, update status, run history, and selected run details without browser-page navigation.',
};

/**
 * Describes a tab hosted by the Workbench desktop shell.
 */
export interface WorkbenchTab {
  /**
   * Stores the stable tab identity used by React keys, tab controls, and fallback recovery.
   */
  readonly id: WorkbenchTabId;

  /**
   * Provides the readable tab title shown in the work area.
   */
  readonly title: string;

  /**
   * Associates the tab with the activity that introduced or owns its placeholder context.
   */
  readonly activityId: WorkbenchActivityId;

  /**
   * Indicates whether later shell actions may close the tab without removing the required start tab.
   */
  readonly isClosable: boolean;

  /**
   * Explains what the placeholder tab represents without inventing feature data.
   */
  readonly placeholderSummary: string;
}

/**
 * Captures shell layout switches that are local to the browser and not server state.
 */
export interface WorkbenchPanelState {
  /**
   * Indicates whether the primary sidebar should be visually collapsed when that behavior is enabled.
   */
  readonly isSidebarCollapsed: boolean;

  /**
   * Indicates whether the bottom panel placeholder should be visible when later slices render it.
   */
  readonly isBottomPanelVisible: boolean;

  /**
   * Indicates whether the command palette placeholder is open when later slices implement commands.
   */
  readonly isCommandPaletteVisible: boolean;

  /**
   * Stores the current primary-sidebar width as a percentage of the content region.
   */
  readonly sidebarWidthPercent: number;

  /**
   * Stores the current bottom-panel height as a percentage of the content region.
   */
  readonly bottomPanelHeightPercent: number;
}

/**
 * Stores the complete Work Item 1 shell state that stays inside the frontend runtime.
 */
export interface WorkbenchState {
  /**
   * Tracks the currently selected activity in the left activity rail.
   */
  readonly activeActivityId: WorkbenchActivityId;

  /**
   * Lists tabs currently hosted in the workbench work area.
   */
  readonly openTabs: readonly WorkbenchTab[];

  /**
   * Tracks the active workbench tab and recovers to the start tab when invalid.
   */
  readonly activeTabId: WorkbenchTabId;

  /**
   * Stores local visibility and placeholder panel switches for the desktop shell.
   */
  readonly panels: WorkbenchPanelState;
}

/**
 * Describes every action that can update the Work Item 1 local shell state.
 */
export type WorkbenchAction =
  | { readonly type: 'selectActivity'; readonly activityId: string }
  | { readonly type: 'openOrFocusTab'; readonly tab: WorkbenchTab }
  | { readonly type: 'selectTab'; readonly tabId: string }
  | { readonly type: 'closeTab'; readonly tabId: string }
  | { readonly type: 'toggleSidebarCollapsed' }
  | { readonly type: 'setSidebarCollapsed'; readonly isCollapsed: boolean }
  | { readonly type: 'toggleBottomPanel' }
  | { readonly type: 'setBottomPanelVisible'; readonly isVisible: boolean }
  | { readonly type: 'setSidebarWidthPercent'; readonly widthPercent: number }
  | { readonly type: 'setBottomPanelHeightPercent'; readonly heightPercent: number }
  | { readonly type: 'resetLayoutPreferences' }
  | { readonly type: 'setCommandPaletteVisible'; readonly isVisible: boolean };

/**
 * Exposes the local shell state and safe state-transition helpers to workbench components.
 */
export interface WorkbenchStore {
  /**
   * Provides the current immutable workbench state snapshot.
   */
  readonly state: WorkbenchState;

  /**
   * Selects an activity by stable identifier and falls back safely for unknown values.
   */
  readonly selectActivity: (activityId: string) => void;

  /**
   * Opens a placeholder tab or focuses it when it already exists.
   */
  readonly openOrFocusTab: (tab: WorkbenchTab) => void;

  /**
   * Selects an existing tab and falls back safely when the requested tab does not exist.
   */
  readonly selectTab: (tabId: string) => void;

  /**
   * Closes a closable tab while preserving the required default start tab.
   */
  readonly closeTab: (tabId: string) => void;

  /**
   * Toggles the primary sidebar collapsed placeholder state.
   */
  readonly toggleSidebarCollapsed: () => void;

  /**
   * Toggles the bottom panel placeholder visibility state.
   */
  readonly toggleBottomPanel: () => void;

  /**
   * Shows the bottom panel region when a shell action needs to focus contextual workbench state.
   */
  readonly showBottomPanel: () => void;

  /**
   * Hides the bottom panel region without discarding any future panel content state.
   */
  readonly hideBottomPanel: () => void;

  /**
   * Stores a validated primary-sidebar width percentage after user resize.
   */
  readonly setSidebarWidthPercent: (widthPercent: number) => void;

  /**
   * Stores a validated bottom-panel height percentage after user resize.
   */
  readonly setBottomPanelHeightPercent: (heightPercent: number) => void;

  /**
   * Restores default layout preferences in memory and clears persisted browser-local values.
   */
  readonly resetLayoutPreferences: () => void;

  /**
   * Sets command palette placeholder visibility for later command-palette work.
   */
  readonly setCommandPaletteVisible: (isVisible: boolean) => void;
}

/**
 * Names the required default tab that must always be recoverable in the shell.
 */
export const defaultWorkbenchTabId: WorkbenchTabId = snapshotWorkspaceTabId;

/**
 * Provides the default local panel switches for the first shell slice.
 */
const defaultPanelState: WorkbenchPanelState = {
  isSidebarCollapsed: false,
  isBottomPanelVisible: false,
  isCommandPaletteVisible: false,
  sidebarWidthPercent: defaultWorkbenchPreferences.sidebarWidthPercent,
  bottomPanelHeightPercent: defaultWorkbenchPreferences.bottomPanelHeightPercent,
};

/**
 * Stores the active workbench context for components rendered under WorkbenchStoreProvider.
 */
const WorkbenchStoreContext = createContext<WorkbenchStore | undefined>(undefined);

/**
 * Creates a fresh default state object for the local workbench shell.
 *
 * @returns The default workbench state with the Snapshot activity and stable workspace tab.
 */
export function getDefaultWorkbenchState(): WorkbenchState {
  // A factory avoids accidental mutation sharing between tests, server rendering, and browser sessions.
  return {
    activeActivityId: getDefaultWorkbenchActivityId(),
    openTabs: [snapshotWorkspaceTab],
    activeTabId: defaultWorkbenchTabId,
    panels: defaultPanelState,
  };
}

/**
 * Creates a safe workbench state snapshot from validated persisted preferences.
 *
 * @param preferences The validated browser-local preference document to apply to defaults.
 * @returns A workbench state snapshot that preserves required tabs and safe layout preferences.
 */
export function getWorkbenchStateFromPreferences(preferences: WorkbenchPreferences): WorkbenchState {
  // Preferences are intentionally narrow and cannot recreate tabs or feature data. They only
  // hydrate shell chrome decisions while preserving the required start tab and safe defaults.
  return {
    ...getDefaultWorkbenchState(),
    activeActivityId: preferences.activeActivityId,
    panels: {
      ...defaultPanelState,
      isSidebarCollapsed: preferences.isSidebarCollapsed,
      isBottomPanelVisible: preferences.isBottomPanelVisible,
      sidebarWidthPercent: preferences.sidebarWidthPercent,
      bottomPanelHeightPercent: preferences.bottomPanelHeightPercent,
    },
  };
}

/**
 * Converts the current workbench state into the safe browser-local preference shape.
 *
 * @param state The current local workbench state snapshot.
 * @returns A validated preference document containing only durable shell layout choices.
 */
export function createWorkbenchPreferencesFromState(state: WorkbenchState): WorkbenchPreferences {
  // The conversion deliberately omits tabs, API configuration, diagnostics, and feature data so
  // persistence stays limited to non-sensitive layout preferences.
  return {
    version: defaultWorkbenchPreferences.version,
    sidebarWidthPercent: normalizePercentPreference(
      state.panels.sidebarWidthPercent,
      defaultWorkbenchPreferences.sidebarWidthPercent,
      minimumSidebarWidthPercent,
      maximumSidebarWidthPercent,
    ),
    bottomPanelHeightPercent: normalizePercentPreference(
      state.panels.bottomPanelHeightPercent,
      defaultWorkbenchPreferences.bottomPanelHeightPercent,
      minimumBottomPanelHeightPercent,
      maximumBottomPanelHeightPercent,
    ),
    isSidebarCollapsed: state.panels.isSidebarCollapsed,
    isBottomPanelVisible: state.panels.isBottomPanelVisible,
    activeActivityId: isWorkbenchActivityId(state.activeActivityId)
      ? state.activeActivityId
      : defaultWorkbenchPreferences.activeActivityId,
  };
}

/**
 * Selects a safe active tab identifier from an available tab collection.
 *
 * @param requestedTabId The tab identifier requested by a user action or reducer path.
 * @param openTabs The current open tab collection used to validate the request.
 * @returns The requested tab when present, otherwise the default start tab identity.
 */
function resolveActiveTabId(requestedTabId: string, openTabs: readonly WorkbenchTab[]): WorkbenchTabId {
  // Invalid tab identifiers can occur through stale callbacks or later persistence, so the
  // reducer always normalizes them to the required start tab instead of throwing during render.
  return openTabs.some((tab) => tab.id === requestedTabId) ? requestedTabId : defaultWorkbenchTabId;
}

/**
 * Ensures the required start tab exists exactly once in the provided tab collection.
 *
 * @param openTabs The candidate tab collection produced by a reducer transition.
 * @returns A normalized collection that always begins with the required start tab.
 */
function ensureDefaultTab(openTabs: readonly WorkbenchTab[]): readonly WorkbenchTab[] {
  // The start tab is not closable and acts as the recovery destination for invalid state, so it
  // is restored if a future action or persistence path accidentally omits it.
  const nonDefaultTabs = openTabs.filter((tab) => tab.id !== defaultWorkbenchTabId);

  return [snapshotWorkspaceTab, ...nonDefaultTabs];
}

/**
 * Applies one local workbench action to an immutable state snapshot.
 *
 * @param state The current workbench state before the action is applied.
 * @param action The user or shell action that should produce the next state.
 * @returns The next safe workbench state after validation and fallback handling.
 */
export function reduceWorkbenchState(state: WorkbenchState, action: WorkbenchAction): WorkbenchState {
  // The reducer owns all fallback behavior so components remain simple view code and invalid
  // identifiers cannot crash the shell or navigate away from the desktop frame.
  switch (action.type) {
    case 'selectActivity': {
      const activeActivityId = isWorkbenchActivityId(action.activityId)
        ? action.activityId
        : getDefaultWorkbenchActivityId();

      if (activeActivityId === 'snapshots') {
        // Selecting the Snapshot workspace activity focuses the durable feature tab immediately so
        // the main work area stays on the primary operational surface.
        const openTabs = ensureDefaultTab([...state.openTabs, snapshotWorkspaceTab]);

        return {
          ...state,
          activeActivityId,
          openTabs,
          activeTabId: resolveActiveTabId(snapshotWorkspaceTab.id, openTabs),
        };
      }

      return {
        ...state,
        activeActivityId,
      };
    }

    case 'openOrFocusTab': {
      const existingTab = state.openTabs.find((tab) => tab.id === action.tab.id);
      const openTabs = existingTab === undefined
        ? ensureDefaultTab([...state.openTabs, action.tab])
        : ensureDefaultTab(state.openTabs);

      return {
        ...state,
        openTabs,
        activeTabId: resolveActiveTabId(action.tab.id, openTabs),
      };
    }

    case 'selectTab': {
      return {
        ...state,
        activeTabId: resolveActiveTabId(action.tabId, state.openTabs),
      };
    }

    case 'closeTab': {
      const openTabs = ensureDefaultTab(state.openTabs.filter((tab) => tab.id !== action.tabId || !tab.isClosable));
      const activeTabId = state.activeTabId === action.tabId
        ? defaultWorkbenchTabId
        : resolveActiveTabId(state.activeTabId, openTabs);

      return {
        ...state,
        openTabs,
        activeTabId,
      };
    }

    case 'toggleSidebarCollapsed': {
      return {
        ...state,
        panels: {
          ...state.panels,
          isSidebarCollapsed: !state.panels.isSidebarCollapsed,
        },
      };
    }

    case 'setSidebarCollapsed': {
      return {
        ...state,
        panels: {
          ...state.panels,
          isSidebarCollapsed: action.isCollapsed,
        },
      };
    }

    case 'toggleBottomPanel': {
      return {
        ...state,
        panels: {
          ...state.panels,
          isBottomPanelVisible: !state.panels.isBottomPanelVisible,
        },
      };
    }

    case 'setBottomPanelVisible': {
      return {
        ...state,
        panels: {
          ...state.panels,
          isBottomPanelVisible: action.isVisible,
        },
      };
    }

    case 'setSidebarWidthPercent': {
      return {
        ...state,
        panels: {
          ...state.panels,
          sidebarWidthPercent: normalizePercentPreference(
            action.widthPercent,
            state.panels.sidebarWidthPercent,
            minimumSidebarWidthPercent,
            maximumSidebarWidthPercent,
          ),
        },
      };
    }

    case 'setBottomPanelHeightPercent': {
      return {
        ...state,
        panels: {
          ...state.panels,
          bottomPanelHeightPercent: normalizePercentPreference(
            action.heightPercent,
            state.panels.bottomPanelHeightPercent,
            minimumBottomPanelHeightPercent,
            maximumBottomPanelHeightPercent,
          ),
        },
      };
    }

    case 'resetLayoutPreferences': {
      return {
        ...state,
        activeActivityId: defaultWorkbenchPreferences.activeActivityId,
        panels: {
          ...state.panels,
          isSidebarCollapsed: defaultWorkbenchPreferences.isSidebarCollapsed,
          isBottomPanelVisible: defaultWorkbenchPreferences.isBottomPanelVisible,
          sidebarWidthPercent: defaultWorkbenchPreferences.sidebarWidthPercent,
          bottomPanelHeightPercent: defaultWorkbenchPreferences.bottomPanelHeightPercent,
        },
      };
    }

    case 'setCommandPaletteVisible': {
      return {
        ...state,
        panels: {
          ...state.panels,
          isCommandPaletteVisible: action.isVisible,
        },
      };
    }
  }
}

/**
 * Provides local workbench state to descendant shell components.
 *
 * @param props Contains the descendants that should consume the local workbench store.
 * @param props.children The React nodes rendered beneath the workbench store provider.
 * @returns The provider-wrapped workbench subtree.
 */
export function WorkbenchStoreProvider({ children }: { readonly children: ReactNode }) {
  // useReducer centralizes shell actions and gives later work items a single place to attach
  // persistence, command execution, and layout recovery without introducing competing stores.
  const [state, dispatch] = useReducer(
    reduceWorkbenchState,
    undefined,
    () => getWorkbenchStateFromPreferences(loadWorkbenchPreferences()),
  );

  useEffect(() => {
    // Persistence is attached at the provider boundary so reducer tests remain pure while the
    // browser shell still saves validated layout choices after each state transition.
    saveWorkbenchPreferences(createWorkbenchPreferencesFromState(state));
  }, [state]);

  /**
   * Dispatches an activity selection request through the validated reducer path.
   *
   * @param activityId The activity identifier requested by the rail or a later command.
   */
  const selectActivity = useCallback((activityId: string): void => {
    dispatch({ type: 'selectActivity', activityId });
  }, []);

  /**
   * Dispatches a request to open a new tab or focus an existing tab.
   *
   * @param tab The placeholder tab descriptor that should become active.
   */
  const openOrFocusTab = useCallback((tab: WorkbenchTab): void => {
    dispatch({ type: 'openOrFocusTab', tab });
  }, []);

  /**
   * Dispatches a tab selection request through the safe fallback path.
   *
   * @param tabId The requested tab identifier from a work area tab control.
   */
  const selectTab = useCallback((tabId: string): void => {
    dispatch({ type: 'selectTab', tabId });
  }, []);

  /**
   * Dispatches a tab close request while preserving non-closable required tabs.
   *
   * @param tabId The tab identifier requested for closure.
   */
  const closeTab = useCallback((tabId: string): void => {
    dispatch({ type: 'closeTab', tabId });
  }, []);

  /**
   * Dispatches a sidebar collapsed-state toggle for the shell layout seam.
   */
  const toggleSidebarCollapsed = useCallback((): void => {
    dispatch({ type: 'toggleSidebarCollapsed' });
  }, []);

  /**
   * Dispatches a bottom-panel visibility toggle for the later bottom-panel seam.
   */
  const toggleBottomPanel = useCallback((): void => {
    dispatch({ type: 'toggleBottomPanel' });
  }, []);

  /**
   * Dispatches a request to show the bottom panel region.
   */
  const showBottomPanel = useCallback((): void => {
    // Explicit show and hide actions let controls communicate intent without having to inspect
    // the current state and risk toggling the panel in the wrong direction.
    dispatch({ type: 'setBottomPanelVisible', isVisible: true });
  }, []);

  /**
   * Dispatches a request to hide the bottom panel region.
   */
  const hideBottomPanel = useCallback((): void => {
    // Hiding preserves panel sizing so users can restore the same layout in the same session.
    dispatch({ type: 'setBottomPanelVisible', isVisible: false });
  }, []);

  /**
   * Dispatches a validated sidebar width update after resize input.
   *
   * @param widthPercent The requested sidebar width percentage from pointer or keyboard input.
   */
  const setSidebarWidthPercent = useCallback((widthPercent: number): void => {
    dispatch({ type: 'setSidebarWidthPercent', widthPercent });
  }, []);

  /**
   * Dispatches a validated bottom-panel height update after resize input.
   *
   * @param heightPercent The requested bottom-panel height percentage from pointer or keyboard input.
   */
  const setBottomPanelHeightPercent = useCallback((heightPercent: number): void => {
    dispatch({ type: 'setBottomPanelHeightPercent', heightPercent });
  }, []);

  /**
   * Clears persisted layout preferences and restores default shell chrome state.
   */
  const resetLayoutPreferencesAction = useCallback((): void => {
    // Storage reset is best-effort and paired with an in-memory reducer reset so the current
    // shell immediately reflects defaults even when localStorage is unavailable.
    resetWorkbenchPreferences();
    dispatch({ type: 'resetLayoutPreferences' });
  }, []);

  /**
   * Dispatches command-palette visibility changes for later command-palette work.
   *
   * @param isVisible Indicates whether the command palette placeholder should be considered open.
   */
  const setCommandPaletteVisible = useCallback((isVisible: boolean): void => {
    dispatch({ type: 'setCommandPaletteVisible', isVisible });
  }, []);

  const store = useMemo<WorkbenchStore>(() => {
    // Memoizing the store keeps React consumers from re-rendering for stable action identities
    // unless the state snapshot itself changes.
    return {
      state,
      selectActivity,
      openOrFocusTab,
      selectTab,
      closeTab,
      toggleSidebarCollapsed,
      toggleBottomPanel,
      showBottomPanel,
      hideBottomPanel,
      setSidebarWidthPercent,
      setBottomPanelHeightPercent,
      resetLayoutPreferences: resetLayoutPreferencesAction,
      setCommandPaletteVisible,
    };
  }, [closeTab, hideBottomPanel, openOrFocusTab, resetLayoutPreferencesAction, selectActivity, selectTab, setBottomPanelHeightPercent, setCommandPaletteVisible, setSidebarWidthPercent, showBottomPanel, state, toggleBottomPanel, toggleSidebarCollapsed]);

  return <WorkbenchStoreContext.Provider value={store}>{children}</WorkbenchStoreContext.Provider>;
}

/**
 * Reads the current local workbench store from React context.
 *
 * @returns The active workbench store for shell components.
 */
export function useWorkbenchStore(): WorkbenchStore {
  // Throwing here is intentional because using shell state outside the provider is a developer
  // composition error; runtime invalid identifiers still recover inside the reducer itself.
  const store = useContext(WorkbenchStoreContext);

  if (store === undefined) {
    throw new Error('Workbench store is unavailable because WorkbenchStoreProvider is missing.');
  }

  return store;
}
