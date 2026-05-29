import { getDefaultWorkbenchActivityId, isWorkbenchActivityId, type WorkbenchActivityId } from '@/components/workbench/workbenchActivities';

/**
 * Identifies the browser storage key reserved for ArchonExplorer workbench preferences.
 */
export const workbenchPreferenceStorageKey = 'archonexplorer.workbench.preferences';

/**
 * Identifies the current persisted preference document version.
 */
export const currentWorkbenchPreferenceVersion = 1;

/**
 * Describes the browser-local layout preferences that are safe to persist.
 */
export interface WorkbenchPreferences {
  /**
   * Tracks the persisted document version so future shapes can fall back safely.
   */
  readonly version: typeof currentWorkbenchPreferenceVersion;

  /**
   * Stores the primary sidebar width as a viewport percentage, excluding the activity rail.
   */
  readonly sidebarWidthPercent: number;

  /**
   * Stores the bottom panel height as a percentage of the vertical content region.
   */
  readonly bottomPanelHeightPercent: number;

  /**
   * Indicates whether the sidebar is collapsed in the local shell layout.
   */
  readonly isSidebarCollapsed: boolean;

  /**
   * Indicates whether the bottom panel is visible in the local shell layout.
   */
  readonly isBottomPanelVisible: boolean;

  /**
   * Stores the last selected workbench activity when the identifier is safe and known.
   */
  readonly activeActivityId: WorkbenchActivityId;
}

/**
 * Defines the smallest supported sidebar size as a percentage of the shell content width.
 */
export const minimumSidebarWidthPercent = 18;

/**
 * Defines the largest supported sidebar size as a percentage of the shell content width.
 */
export const maximumSidebarWidthPercent = 42;

/**
 * Defines the smallest supported bottom-panel size as a percentage of the shell content height.
 */
export const minimumBottomPanelHeightPercent = 18;

/**
 * Defines the largest supported bottom-panel size as a percentage of the shell content height.
 */
export const maximumBottomPanelHeightPercent = 50;

/**
 * Provides the default persisted layout preferences for a new or recovered shell session.
 */
export const defaultWorkbenchPreferences: WorkbenchPreferences = {
  version: currentWorkbenchPreferenceVersion,
  sidebarWidthPercent: 26,
  bottomPanelHeightPercent: 30,
  isSidebarCollapsed: false,
  isBottomPanelVisible: false,
  activeActivityId: getDefaultWorkbenchActivityId(),
};

/**
 * Defines the small storage surface used by the preference helpers.
 */
export interface WorkbenchPreferenceStorage {
  /**
   * Reads a stored preference document by key.
   *
   * @param key The stable local-storage key used for workbench preferences.
   * @returns The stored JSON string when available, otherwise null.
   */
  readonly getItem: (key: string) => string | null;

  /**
   * Writes a stored preference document by key.
   *
   * @param key The stable local-storage key used for workbench preferences.
   * @param value The serialized safe preference document to store.
   */
  readonly setItem: (key: string, value: string) => void;

  /**
   * Removes a stored preference document by key.
   *
   * @param key The stable local-storage key used for workbench preferences.
   */
  readonly removeItem: (key: string) => void;
}

/**
 * Restricts a numeric preference to its supported range and default fallback.
 *
 * @param value The unknown value read from a persisted preference document.
 * @param fallback The safe default used when the stored value is invalid.
 * @param minimum The smallest accepted value for this preference.
 * @param maximum The largest accepted value for this preference.
 * @returns A finite value inside the accepted range.
 */
export function normalizePercentPreference(value: unknown, fallback: number, minimum: number, maximum: number): number {
  // Stored preferences are untrusted browser-local data, so numeric values must be finite and
  // clamped before they influence layout styles or pointer-driven resize calculations.
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return fallback;
  }

  return Math.min(Math.max(value, minimum), maximum);
}

/**
 * Creates a safe preference document from untrusted parsed storage data.
 *
 * @param value The parsed preference candidate read from browser-local storage.
 * @returns A validated preference document, or defaults when the document is missing or incompatible.
 */
export function parseWorkbenchPreferences(value: unknown): WorkbenchPreferences {
  // Persistence deliberately fail-closes to defaults for any unknown document version or shape.
  // The shell should recover predictably rather than preserving stale values that might no
  // longer match current layout constraints.
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return defaultWorkbenchPreferences;
  }

  const candidate = value as Record<string, unknown>;

  if (candidate.version !== currentWorkbenchPreferenceVersion) {
    return defaultWorkbenchPreferences;
  }

  return {
    version: currentWorkbenchPreferenceVersion,
    sidebarWidthPercent: normalizePercentPreference(
      candidate.sidebarWidthPercent,
      defaultWorkbenchPreferences.sidebarWidthPercent,
      minimumSidebarWidthPercent,
      maximumSidebarWidthPercent,
    ),
    bottomPanelHeightPercent: normalizePercentPreference(
      candidate.bottomPanelHeightPercent,
      defaultWorkbenchPreferences.bottomPanelHeightPercent,
      minimumBottomPanelHeightPercent,
      maximumBottomPanelHeightPercent,
    ),
    isSidebarCollapsed: typeof candidate.isSidebarCollapsed === 'boolean'
      ? candidate.isSidebarCollapsed
      : defaultWorkbenchPreferences.isSidebarCollapsed,
    isBottomPanelVisible: typeof candidate.isBottomPanelVisible === 'boolean'
      ? candidate.isBottomPanelVisible
      : defaultWorkbenchPreferences.isBottomPanelVisible,
    activeActivityId: typeof candidate.activeActivityId === 'string' && isWorkbenchActivityId(candidate.activeActivityId)
      ? candidate.activeActivityId
      : defaultWorkbenchPreferences.activeActivityId,
  };
}

/**
 * Returns the browser local-storage object when it is available to the current runtime.
 *
 * @returns The local-storage object for browser sessions, otherwise undefined for server rendering or tests.
 */
export function getBrowserWorkbenchPreferenceStorage(): WorkbenchPreferenceStorage | undefined {
  // Server rendering and some tests do not provide localStorage. Returning undefined keeps the
  // shell runnable and lets callers fall back to in-memory defaults without branching elsewhere.
  return typeof globalThis.localStorage === 'undefined'
    ? undefined
    : globalThis.localStorage;
}

/**
 * Loads workbench preferences from browser-local storage with safe fallback behavior.
 *
 * @param storage Optional storage implementation; defaults to browser localStorage when available.
 * @returns The validated preference document, or defaults if storage is unavailable or invalid.
 */
export function loadWorkbenchPreferences(storage: WorkbenchPreferenceStorage | undefined = getBrowserWorkbenchPreferenceStorage()): WorkbenchPreferences {
  // Storage access can fail in locked-down browser modes, so every read is guarded and treated as
  // an optional enhancement rather than a prerequisite for rendering the workbench shell.
  if (storage === undefined) {
    return defaultWorkbenchPreferences;
  }

  try {
    const storedValue = storage.getItem(workbenchPreferenceStorageKey);

    if (storedValue === null) {
      return defaultWorkbenchPreferences;
    }

    return parseWorkbenchPreferences(JSON.parse(storedValue));
  } catch {
    return defaultWorkbenchPreferences;
  }
}

/**
 * Saves a validated workbench preference document to browser-local storage.
 *
 * @param preferences The safe preference document to persist for later browser sessions.
 * @param storage Optional storage implementation; defaults to browser localStorage when available.
 */
export function saveWorkbenchPreferences(preferences: WorkbenchPreferences, storage: WorkbenchPreferenceStorage | undefined = getBrowserWorkbenchPreferenceStorage()): void {
  // Only the validated preference shape is serialized. Runtime data, secrets, API values, raw
  // diagnostics, and feature records are intentionally absent from the document.
  if (storage === undefined) {
    return;
  }

  try {
    storage.setItem(workbenchPreferenceStorageKey, JSON.stringify(parseWorkbenchPreferences(preferences)));
  } catch {
    // Preference persistence is best-effort; rendering must continue even if the browser rejects writes.
  }
}

/**
 * Removes persisted workbench preferences from browser-local storage.
 *
 * @param storage Optional storage implementation; defaults to browser localStorage when available.
 */
export function resetWorkbenchPreferences(storage: WorkbenchPreferenceStorage | undefined = getBrowserWorkbenchPreferenceStorage()): void {
  // Reset clears only the dedicated workbench preference key, leaving unrelated browser storage and
  // future feature data untouched.
  if (storage === undefined) {
    return;
  }

  try {
    storage.removeItem(workbenchPreferenceStorageKey);
  } catch {
    // Reset should be safe to invoke even when storage is denied; callers can still restore defaults in memory.
  }
}
