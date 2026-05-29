import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { ApiConnectivityState } from '@/api/connectivity';
import { ActivityRail } from '@/components/workbench/ActivityRail';
import { PrimarySidebar } from '@/components/workbench/PrimarySidebar';
import { BottomPanel } from '@/components/workbench/BottomPanel';
import { StatusBarContent } from '@/components/workbench/StatusBar';
import { TabbedWorkArea } from '@/components/workbench/TabbedWorkArea';
import { WorkbenchShell } from '@/components/workbench/WorkbenchShell';
import { ApplicationProviders } from '@/providers/ApplicationProviders';
import { ExtractionCenterStoreProvider } from '@/state/extractionCenterStore';
import {
  createWorkbenchPreferencesFromState,
  getDefaultWorkbenchState,
  getWorkbenchStateFromPreferences,
  reduceWorkbenchState,
} from '@/state/workbenchStore';
import {
  defaultWorkbenchPreferences,
  loadWorkbenchPreferences,
  parseWorkbenchPreferences,
  resetWorkbenchPreferences,
  saveWorkbenchPreferences,
  workbenchPreferenceStorageKey,
  type WorkbenchPreferenceStorage,
} from '@/lib/workbenchPersistence';

/**
 * Provides a deterministic safe API state for shell server-rendering tests.
 */
const safeConnectivityState: ApiConnectivityState = {
  status: 'unconfigured',
  label: 'API base URL not configured',
  description: 'Configure the Archon API base URL before connectivity checks run.',
  retryable: false,
};

/**
 * Renders the Work Item 1 shell with deterministic runtime inputs.
 *
 * @returns Static HTML used for shell composition assertions without a browser DOM dependency.
 */
function renderShellMarkup(): string {
  // Server rendering keeps these tests aligned with the existing frontend test estate while
  // still proving the composed shell exposes its landmarks, placeholders, and default tab.
  return renderToStaticMarkup(
    <ApplicationProviders>
      <WorkbenchShell
        apiConfiguration={{ isConfigured: false }}
        connectivityState={safeConnectivityState}
      />
    </ApplicationProviders>,
  );
}

/**
 * Verifies the first runnable workbench shell slice.
 */
describe('workbench shell rendering', () => {
  /**
   * Confirms the shell renders durable desktop workbench regions and the default start tab.
   */
  it('renders the activity rail, primary sidebar, tabbed work area, and default start tab', () => {
    const markup = renderShellMarkup();

    expect(markup).toContain('ArchonExplorer');
    expect(markup).toContain('aria-label="ArchonExplorer workbench activities"');
    expect(markup).toContain('aria-label="Primary workbench sidebar"');
    expect(markup).toContain('aria-label="Workbench tabs"');
    expect(markup).toContain('Workbench Start');
  });

  /**
   * Confirms placeholders remain honest about unavailable feature workflows.
   */
  it('renders safe placeholder text without implying unavailable features are complete', () => {
    const markup = renderShellMarkup();

    expect(markup).toContain('Extraction history is available in this slice. Snapshot, search, project, finding, diagnostics, submission, and run-monitoring workflows arrive in later work packages.');
    expect(markup).toContain('No extraction runs, snapshots, graph data, search results, evidence, or findings are loaded in this shell slice.');
    expect(markup).not.toContain('Password=');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('Neo4j driver');
  });

  /**
   * Confirms selecting Extraction Center opens the API-backed feature tab.
   */
  it('opens the Extraction Center tab when its activity is selected', () => {
    const initialState = getDefaultWorkbenchState();
    const selectedState = reduceWorkbenchState(initialState, { type: 'selectActivity', activityId: 'extraction-center' });

    expect(selectedState.activeActivityId).toBe('extraction-center');
    expect(selectedState.activeTabId).toBe('extraction-center');
    expect(selectedState.openTabs.some((tab) => tab.id === 'extraction-center' && tab.title === 'Extraction Center')).toBe(true);
  });
});

/**
 * Verifies local state transitions that drive shell activity navigation.
 */
describe('workbench activity navigation state', () => {
  /**
   * Confirms selecting an activity changes sidebar context without browser navigation.
   */
  it('selects an activity and renders the matching sidebar placeholder', () => {
    const initialState = getDefaultWorkbenchState();
    const selectedState = reduceWorkbenchState(initialState, { type: 'selectActivity', activityId: 'snapshots' });
    const sidebarMarkup = renderToStaticMarkup(
      <PrimarySidebar activeActivityId={selectedState.activeActivityId} />,
    );

    expect(selectedState.activeActivityId).toBe('snapshots');
    expect(sidebarMarkup).toContain('Snapshots');
    expect(sidebarMarkup).toContain('Snapshot administration arrives in a later work package.');
  });

  /**
   * Confirms invalid activity identifiers recover to the default dashboard activity.
   */
  it('recovers to the dashboard when an invalid activity is selected', () => {
    const initialState = getDefaultWorkbenchState();
    const selectedState = reduceWorkbenchState(initialState, { type: 'selectActivity', activityId: 'missing-area' });

    expect(selectedState.activeActivityId).toBe('dashboard');
  });

  /**
   * Confirms the activity rail exposes keyboard-reachable controls for each roadmap activity.
   */
  it('renders enabled controls for roadmap-aligned activities', () => {
    const state = getDefaultWorkbenchState();
    const markup = renderToStaticMarkup(
      <ActivityRail activeActivityId={state.activeActivityId} onSelectActivity={() => undefined} />,
    );

    expect(markup).toContain('type="button"');
    expect(markup).toContain('Dashboard');
    expect(markup).toContain('Extraction Center');
    expect(markup).toContain('Snapshots');
    expect(markup).toContain('Search');
    expect(markup).toContain('Projects');
    expect(markup).toContain('Findings');
    expect(markup).toContain('Diagnostics');
  });
});

/**
 * Verifies local tab state for the default tabbed work area.
 */
describe('workbench tab state', () => {
  /**
   * Confirms the default workbench state contains one stable start tab.
   */
  it('creates a stable default start tab', () => {
    const state = getDefaultWorkbenchState();

    expect(state.openTabs).toHaveLength(1);
    expect(state.openTabs[0]?.id).toBe('workbench-start');
    expect(state.activeTabId).toBe('workbench-start');
  });

  /**
   * Confirms opening an existing placeholder tab focuses it rather than duplicating it.
   */
  it('focuses an existing placeholder tab instead of duplicating it', () => {
    const initialState = getDefaultWorkbenchState();
    const openedState = reduceWorkbenchState(initialState, {
      type: 'openOrFocusTab',
      tab: {
        id: 'dashboard-overview',
        title: 'Dashboard Overview',
        activityId: 'dashboard',
        isClosable: true,
        placeholderSummary: 'Dashboard placeholder.',
      },
    });
    const focusedState = reduceWorkbenchState(openedState, {
      type: 'openOrFocusTab',
      tab: {
        id: 'dashboard-overview',
        title: 'Dashboard Overview',
        activityId: 'dashboard',
        isClosable: true,
        placeholderSummary: 'Dashboard placeholder.',
      },
    });

    expect(focusedState.openTabs).toHaveLength(2);
    expect(focusedState.activeTabId).toBe('dashboard-overview');
  });

  /**
   * Confirms invalid tab selections fall back to the default start tab.
   */
  it('recovers to the default start tab when a missing tab is selected', () => {
    const state = reduceWorkbenchState(getDefaultWorkbenchState(), { type: 'selectTab', tabId: 'missing-tab' });

    expect(state.activeTabId).toBe('workbench-start');
  });

  /**
   * Confirms the tabbed work area renders accessible tab semantics and placeholder content.
   */
  it('renders the active tab panel with safe placeholder content', () => {
    const state = getDefaultWorkbenchState();
    const markup = renderToStaticMarkup(
      <TabbedWorkArea tabs={state.openTabs} activeTabId={state.activeTabId} onSelectTab={() => undefined} />,
    );

    expect(markup).toContain('role="tablist"');
    expect(markup).toContain('role="tab"');
    expect(markup).toContain('role="tabpanel"');
    expect(markup).toContain('Workbench Start');
    expect(markup).toContain('No extraction runs, snapshots, graph data, search results, evidence, or findings are loaded in this shell slice.');
  });
});

/**
 * Verifies status bar composition remains deterministic for shell tests.
 */
describe('workbench status bar placeholders', () => {
  /**
   * Confirms status bar placeholder slots remain safe and explicit.
   */
  it('renders safe shell status placeholders', () => {
    const markup = renderToStaticMarkup(<StatusBarContent connectivityState={safeConnectivityState} />);

    expect(markup).toContain('Active snapshot:');
    expect(markup).toContain('current unavailable');
    expect(markup).toContain('Background work:');
    expect(markup).toContain('none running; bottom panel hidden');
    expect(markup).toContain('Selection:');
    expect(markup).toContain('Dashboard activity selected; no item selected');
  });

  /**
   * Confirms status slots include selected context and panel state as text rather than color alone.
   */
  it('renders selected activity and bottom-panel visibility text', () => {
    const markup = renderToStaticMarkup(
      <StatusBarContent connectivityState={safeConnectivityState} isBottomPanelVisible activeActivityId="snapshots" />,
    );

    expect(markup).toContain('bottom panel visible');
    expect(markup).toContain('Snapshots activity selected; no item selected');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('Password=');
  });
});

/**
 * Verifies browser-local preference persistence for the workbench layout slice.
 */
describe('workbench preference persistence', () => {
  /**
   * Creates an in-memory storage implementation for deterministic persistence tests.
   *
   * @returns A storage implementation and value reader for test assertions.
   */
  function createStorage(): WorkbenchPreferenceStorage & { readonly peek: (key: string) => string | null } {
    // The helper mirrors the small storage surface used by production code without depending on
    // a browser DOM or sharing state between test cases.
    const values = new Map<string, string>();

    return {
      getItem: (key) => values.get(key) ?? null,
      setItem: (key, value) => {
        values.set(key, value);
      },
      removeItem: (key) => {
        values.delete(key);
      },
      peek: (key) => values.get(key) ?? null,
    };
  }

  /**
   * Confirms missing preferences recover to documented defaults.
   */
  it('loads default preferences when no stored value exists', () => {
    const storage = createStorage();

    expect(loadWorkbenchPreferences(storage)).toEqual(defaultWorkbenchPreferences);
  });

  /**
   * Confirms save and load round-trip only the safe preference document shape.
   */
  it('saves and loads validated layout preferences', () => {
    const storage = createStorage();
    const preferences = {
      ...defaultWorkbenchPreferences,
      sidebarWidthPercent: 34,
      bottomPanelHeightPercent: 42,
      isBottomPanelVisible: true,
      activeActivityId: 'search' as const,
    };

    saveWorkbenchPreferences(preferences, storage);

    expect(storage.peek(workbenchPreferenceStorageKey)).not.toContain('Password=');
    expect(loadWorkbenchPreferences(storage)).toEqual(preferences);
  });

  /**
   * Confirms invalid JSON is treated as absent storage rather than a render-blocking failure.
   */
  it('recovers to defaults when stored JSON is invalid', () => {
    const storage = createStorage();
    storage.setItem(workbenchPreferenceStorageKey, '{not-json');

    expect(loadWorkbenchPreferences(storage)).toEqual(defaultWorkbenchPreferences);
  });

  /**
   * Confirms incompatible preference documents fall back or clamp to safe values.
   */
  it('recovers from incompatible stored shapes and unsafe values', () => {
    expect(parseWorkbenchPreferences({ version: 999 })).toEqual(defaultWorkbenchPreferences);
    expect(parseWorkbenchPreferences({
      version: 1,
      sidebarWidthPercent: 2,
      bottomPanelHeightPercent: 99,
      isSidebarCollapsed: 'no',
      isBottomPanelVisible: true,
      activeActivityId: 'missing',
    })).toEqual({
      ...defaultWorkbenchPreferences,
      sidebarWidthPercent: 18,
      bottomPanelHeightPercent: 50,
      isBottomPanelVisible: true,
    });
  });

  /**
   * Confirms reset removes persisted data and state conversion keeps only safe layout choices.
   */
  it('resets stored preferences and creates state from safe preferences', () => {
    const storage = createStorage();
    const preferences = {
      ...defaultWorkbenchPreferences,
      isBottomPanelVisible: true,
      sidebarWidthPercent: 31,
      activeActivityId: 'diagnostics' as const,
    };

    saveWorkbenchPreferences(preferences, storage);
    resetWorkbenchPreferences(storage);

    expect(storage.peek(workbenchPreferenceStorageKey)).toBeNull();

    const hydratedState = getWorkbenchStateFromPreferences(preferences);
    expect(hydratedState.activeActivityId).toBe('diagnostics');
    expect(hydratedState.panels.isBottomPanelVisible).toBe(true);
    expect(hydratedState.openTabs).toHaveLength(1);
    expect(createWorkbenchPreferencesFromState(hydratedState)).toEqual(preferences);
  });
});

/**
 * Verifies bottom-panel state and safe rendering behavior.
 */
describe('workbench bottom panel', () => {
  /**
   * Confirms reducer actions can show, hide, and reset the bottom panel state.
   */
  it('toggles, hides, and resets bottom-panel visibility state', () => {
    const shownState = reduceWorkbenchState(getDefaultWorkbenchState(), { type: 'setBottomPanelVisible', isVisible: true });
    const hiddenState = reduceWorkbenchState(shownState, { type: 'setBottomPanelVisible', isVisible: false });
    const toggledState = reduceWorkbenchState(hiddenState, { type: 'toggleBottomPanel' });
    const resetState = reduceWorkbenchState(toggledState, { type: 'resetLayoutPreferences' });

    expect(shownState.panels.isBottomPanelVisible).toBe(true);
    expect(hiddenState.panels.isBottomPanelVisible).toBe(false);
    expect(toggledState.panels.isBottomPanelVisible).toBe(true);
    expect(resetState.panels.isBottomPanelVisible).toBe(false);
  });

  /**
   * Confirms bottom-panel placeholder copy is safe and does not expose raw diagnostics.
   */
  it('renders safe placeholders for background work, extraction runs, and diagnostics', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterStoreProvider>
        <BottomPanel onHide={() => undefined} />
      </ExtractionCenterStoreProvider>,
    );

    expect(markup).toContain('Background Work');
    expect(markup).toContain('Extraction Runs');
    expect(markup).toContain('Diagnostics');
    expect(markup).toContain('never include stack traces, connection strings, environment variables, raw Cypher, Neo4j internals, or driver details');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('Password=');
  });

  /**
   * Confirms layout resize actions clamp invalid percentages to supported bounds.
   */
  it('clamps layout size changes to supported bounds', () => {
    const wideSidebarState = reduceWorkbenchState(getDefaultWorkbenchState(), { type: 'setSidebarWidthPercent', widthPercent: 99 });
    const shortBottomPanelState = reduceWorkbenchState(getDefaultWorkbenchState(), { type: 'setBottomPanelHeightPercent', heightPercent: 2 });

    expect(wideSidebarState.panels.sidebarWidthPercent).toBe(42);
    expect(shortBottomPanelState.panels.bottomPanelHeightPercent).toBe(18);
  });
});
