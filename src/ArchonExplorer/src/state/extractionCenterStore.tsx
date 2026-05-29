import { createContext, useCallback, useContext, useMemo, useReducer, type ReactNode } from 'react';

/**
 * Stores the stable local focus target names understood by Extraction Center command intents.
 */
export type ExtractionCenterFocusTarget = 'new-request-form' | 'active-background-run';

/**
 * Describes one background run tracked by browser-local Extraction Center workflow state.
 */
export interface TrackedExtractionRun {
  /**
   * Contains the public extraction run identifier used by TanStack Query keys and API routes.
   */
  readonly runId: string;

  /**
   * Records whether a terminal run has been explicitly acknowledged in the bottom panel.
   */
  readonly isAcknowledged: boolean;

  /**
   * Stores the last status for which a transition notification was published.
   */
  readonly lastNotifiedStatus?: string;
}

/**
 * Captures browser-local Extraction Center workflow state that must be shared across shell regions.
 */
export interface ExtractionCenterState {
  /**
   * Lists tracked runs by identifier without copying full server status responses into local state.
   */
  readonly trackedRuns: readonly TrackedExtractionRun[];

  /**
   * Carries the selected run identifier requested by the page, bottom panel, or command palette.
   */
  readonly selectedRunId?: string;

  /**
   * Carries a monotonically increasing request marker used to focus the start form from commands.
   */
  readonly formFocusRequestId: number;

  /**
   * Carries a monotonically increasing request marker used to refresh history from commands.
   */
  readonly historyRefreshRequestId: number;
}

/**
 * Describes reducer actions for local Extraction Center workflow state.
 */
type ExtractionCenterAction =
  | { readonly type: 'trackRun'; readonly runId: string }
  | { readonly type: 'selectRun'; readonly runId: string }
  | { readonly type: 'acknowledgeRun'; readonly runId: string }
  | { readonly type: 'recordNotifiedStatus'; readonly runId: string; readonly status: string }
  | { readonly type: 'requestFormFocus' }
  | { readonly type: 'requestHistoryRefresh' }
  | { readonly type: 'focusActiveBackgroundRun' };

/**
 * Exposes shared Extraction Center workflow state and command-oriented actions to shell regions.
 */
export interface ExtractionCenterStore {
  /**
   * Provides the immutable current feature workflow state.
   */
  readonly state: ExtractionCenterState;

  /**
   * Tracks a run identifier for bottom-panel monitoring while leaving status data in TanStack Query.
   */
  readonly trackRun: (runId: string) => void;

  /**
   * Selects and tracks a run identifier for page-level detail monitoring.
   */
  readonly selectRun: (runId: string) => void;

  /**
   * Acknowledges a terminal run so the bottom panel can remove it from visible tracked work.
   */
  readonly acknowledgeRun: (runId: string) => void;

  /**
   * Records the most recent status notification published for a run.
   */
  readonly recordNotifiedStatus: (runId: string, status: string) => void;

  /**
   * Requests that the Extraction Center open and focus its new request form.
   */
  readonly requestFormFocus: () => void;

  /**
   * Requests that the Extraction Center refresh recent run history.
   */
  readonly requestHistoryRefresh: () => void;

  /**
   * Selects the first visible active background run when one is available.
   */
  readonly focusActiveBackgroundRun: () => void;
}

/**
 * Defines the default workflow state for the Extraction Center provider.
 */
const defaultExtractionCenterState: ExtractionCenterState = {
  trackedRuns: [],
  formFocusRequestId: 0,
  historyRefreshRequestId: 0,
};

/**
 * Stores the active Extraction Center workflow context for shell and feature components.
 */
const ExtractionCenterStoreContext = createContext<ExtractionCenterStore | undefined>(undefined);

/**
 * Creates a new default state snapshot for tests and provider initialization.
 *
 * @returns The default Extraction Center workflow state with no tracked runs or pending intents.
 */
export function getDefaultExtractionCenterState(): ExtractionCenterState {
  // Returning a fresh object prevents tests from sharing array identity with provider defaults.
  return {
    ...defaultExtractionCenterState,
    trackedRuns: [],
  };
}

/**
 * Normalizes a run identifier before it becomes local workflow state.
 *
 * @param runId The candidate run identifier supplied by page, panel, or command actions.
 * @returns A trimmed identifier when usable; otherwise undefined so callers can ignore it safely.
 */
function normalizeRunId(runId: string): string | undefined {
  // The browser never validates run identifier format beyond requiring non-empty text; ArchonApi
  // remains authoritative for whether a selected identifier exists.
  const normalizedRunId = runId.trim();
  return normalizedRunId.length > 0 ? normalizedRunId : undefined;
}

/**
 * Adds or refreshes a tracked run while preserving acknowledgement and notification state.
 *
 * @param trackedRuns The current tracked run collection.
 * @param runId The normalized run identifier to ensure in the collection.
 * @returns A collection that contains the run exactly once.
 */
function ensureTrackedRun(trackedRuns: readonly TrackedExtractionRun[], runId: string): readonly TrackedExtractionRun[] {
  // Tracking the same run more than once should not create duplicate bottom-panel rows or reset
  // notification memory, so existing entries are preserved verbatim.
  if (trackedRuns.some((run) => run.runId === runId)) {
    return trackedRuns;
  }

  return [...trackedRuns, { runId, isAcknowledged: false }];
}

/**
 * Finds the first unacknowledged run that can be selected as background work.
 *
 * @param trackedRuns The tracked run collection in insertion order.
 * @returns The first run identifier still visible in the background monitor, if any.
 */
function findFirstVisibleRunId(trackedRuns: readonly TrackedExtractionRun[]): string | undefined {
  // The command intentionally uses local visibility state rather than server responses, because
  // full status data remains in TanStack Query and the bottom panel decides whether a terminal row
  // needs acknowledgement.
  return trackedRuns.find((run) => !run.isAcknowledged)?.runId;
}

/**
 * Applies one Extraction Center workflow action to immutable local state.
 *
 * @param state The current local Extraction Center workflow state.
 * @param action The user, panel, page, or command action to apply.
 * @returns The next safe workflow state.
 */
export function reduceExtractionCenterState(state: ExtractionCenterState, action: ExtractionCenterAction): ExtractionCenterState {
  // The reducer keeps command and panel behavior deterministic while deliberately storing only run
  // identifiers, acknowledgement flags, and intent counters rather than API response payloads.
  switch (action.type) {
    case 'trackRun': {
      const runId = normalizeRunId(action.runId);
      if (runId === undefined) {
        return state;
      }

      return {
        ...state,
        trackedRuns: ensureTrackedRun(state.trackedRuns, runId),
      };
    }

    case 'selectRun': {
      const runId = normalizeRunId(action.runId);
      if (runId === undefined) {
        return state;
      }

      return {
        ...state,
        selectedRunId: runId,
        trackedRuns: ensureTrackedRun(state.trackedRuns, runId),
      };
    }

    case 'acknowledgeRun': {
      const runId = normalizeRunId(action.runId);
      if (runId === undefined) {
        return state;
      }

      return {
        ...state,
        trackedRuns: state.trackedRuns.map((run) => run.runId === runId ? { ...run, isAcknowledged: true } : run),
      };
    }

    case 'recordNotifiedStatus': {
      const runId = normalizeRunId(action.runId);
      const status = action.status.trim();
      if (runId === undefined || status.length === 0) {
        return state;
      }

      return {
        ...state,
        trackedRuns: ensureTrackedRun(state.trackedRuns, runId).map((run) => run.runId === runId ? { ...run, lastNotifiedStatus: status } : run),
      };
    }

    case 'requestFormFocus': {
      return {
        ...state,
        formFocusRequestId: state.formFocusRequestId + 1,
      };
    }

    case 'requestHistoryRefresh': {
      return {
        ...state,
        historyRefreshRequestId: state.historyRefreshRequestId + 1,
      };
    }

    case 'focusActiveBackgroundRun': {
      const runId = findFirstVisibleRunId(state.trackedRuns);
      if (runId === undefined) {
        return state;
      }

      return {
        ...state,
        selectedRunId: runId,
        trackedRuns: ensureTrackedRun(state.trackedRuns, runId),
      };
    }
  }
}

/**
 * Provides shared Extraction Center workflow state to the workbench shell subtree.
 *
 * @param props Contains the descendant React nodes that need feature workflow state.
 * @param props.children The React nodes rendered beneath the provider.
 * @returns A provider-wrapped subtree with command, panel, and page workflow coordination.
 */
export function ExtractionCenterStoreProvider({ children }: { readonly children: ReactNode }) {
  // useReducer gives bottom-panel controls, commands, and the Extraction Center page one shared
  // local state machine without moving server-owned extraction responses out of TanStack Query.
  const [state, dispatch] = useReducer(reduceExtractionCenterState, undefined, getDefaultExtractionCenterState);

  /**
   * Dispatches a request to track a run in the bottom-panel monitor.
   *
   * @param runId The public extraction run identifier to track.
   */
  const trackRun = useCallback((runId: string): void => {
    dispatch({ type: 'trackRun', runId });
  }, []);

  /**
   * Dispatches a request to select and track a run.
   *
   * @param runId The public extraction run identifier to select.
   */
  const selectRun = useCallback((runId: string): void => {
    dispatch({ type: 'selectRun', runId });
  }, []);

  /**
   * Dispatches a request to acknowledge terminal background work.
   *
   * @param runId The public extraction run identifier to acknowledge.
   */
  const acknowledgeRun = useCallback((runId: string): void => {
    dispatch({ type: 'acknowledgeRun', runId });
  }, []);

  /**
   * Dispatches a status-notification memory update for one run.
   *
   * @param runId The public extraction run identifier whose notification state changed.
   * @param status The lifecycle status that was just announced safely.
   */
  const recordNotifiedStatus = useCallback((runId: string, status: string): void => {
    dispatch({ type: 'recordNotifiedStatus', runId, status });
  }, []);

  /**
   * Dispatches a command intent to focus the new request form.
   */
  const requestFormFocus = useCallback((): void => {
    dispatch({ type: 'requestFormFocus' });
  }, []);

  /**
   * Dispatches a command intent to refresh recent history.
   */
  const requestHistoryRefresh = useCallback((): void => {
    dispatch({ type: 'requestHistoryRefresh' });
  }, []);

  /**
   * Dispatches a command intent to select the first visible background run.
   */
  const focusActiveBackgroundRun = useCallback((): void => {
    dispatch({ type: 'focusActiveBackgroundRun' });
  }, []);

  const store = useMemo<ExtractionCenterStore>(() => {
    // Memoization keeps callback identities stable for command registration and shell components
    // while still updating consumers whenever the local workflow state changes.
    return {
      state,
      trackRun,
      selectRun,
      acknowledgeRun,
      recordNotifiedStatus,
      requestFormFocus,
      requestHistoryRefresh,
      focusActiveBackgroundRun,
    };
  }, [acknowledgeRun, focusActiveBackgroundRun, recordNotifiedStatus, requestFormFocus, requestHistoryRefresh, selectRun, state, trackRun]);

  return <ExtractionCenterStoreContext.Provider value={store}>{children}</ExtractionCenterStoreContext.Provider>;
}

/**
 * Reads the shared Extraction Center workflow store from React context.
 *
 * @returns The active Extraction Center store used by shell, command, panel, and page components.
 */
export function useExtractionCenterStore(): ExtractionCenterStore {
  // Missing provider usage is a developer composition error because workbench shell composition is
  // responsible for creating one shared feature state container.
  const store = useContext(ExtractionCenterStoreContext);

  if (store === undefined) {
    throw new Error('Extraction Center store is unavailable because ExtractionCenterStoreProvider is missing.');
  }

  return store;
}
