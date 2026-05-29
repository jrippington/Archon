import { Badge } from '@/components/ui/badge';
import { ExtractionCenter } from '@/components/extraction-center/ExtractionCenter';
import type { WorkbenchTab, WorkbenchTabId } from '@/state/workbenchStore';
import { getWorkbenchActivity } from './workbenchActivities';

/**
 * Describes the tabbed work area inputs supplied by the local workbench store.
 */
export interface TabbedWorkAreaProps {
  /**
   * Lists the tabs currently open in the local workbench shell.
   */
  readonly tabs: readonly WorkbenchTab[];

  /**
   * Identifies the currently selected tab.
   */
  readonly activeTabId: WorkbenchTabId;

  /**
   * Selects a tab when a user activates a tab control.
   */
  readonly onSelectTab: (tabId: string) => void;
}

/**
 * Renders the desktop-style tab strip and active tab panel for the workbench shell.
 *
 * @param props Contains open tabs, active identity, and the local tab-selection callback.
 * @param props.tabs The tab descriptors rendered in the accessible tab list.
 * @param props.activeTabId The currently active tab identifier.
 * @param props.onSelectTab Callback invoked when a tab control is selected.
 * @returns A tabbed work area with safe placeholder content for the selected tab.
 */
export function TabbedWorkArea({ tabs, activeTabId, onSelectTab }: TabbedWorkAreaProps) {
  // Invalid active identifiers can be produced by stale callbacks or future persisted data;
  // rendering falls back to the first available tab so the work area remains stable.
  const activeTab = tabs.find((tab) => tab.id === activeTabId) ?? tabs[0];

  return (
    <main className="workbench-workspace" aria-labelledby="workbench-work-area-title">
      <section className="workbench-tabs" aria-label="Workbench tabs">
        <div className="workbench-tabs__bar" role="tablist" aria-label="Open workbench tabs">
          {tabs.map((tab) => {
            const isSelected = tab.id === activeTab?.id;

            return (
              <button
                aria-controls={`workbench-tabpanel-${tab.id}`}
                aria-selected={isSelected}
                className="workbench-tabs__tab"
                id={`workbench-tab-${tab.id}`}
                key={tab.id}
                onClick={() => onSelectTab(tab.id)}
                role="tab"
                tabIndex={isSelected ? 0 : -1}
                type="button"
              >
                <span>{tab.title}</span>
                {!tab.isClosable && <Badge variant="outline">Required</Badge>}
              </button>
            );
          })}
        </div>
        {activeTab !== undefined && <WorkbenchTabPanel tab={activeTab} />}
      </section>
    </main>
  );
}

/**
 * Describes the active tab panel inputs.
 */
interface WorkbenchTabPanelProps {
  /**
   * Provides the selected tab descriptor rendered in the panel body.
   */
  readonly tab: WorkbenchTab;
}

/**
 * Renders the content panel for the active workbench tab.
 *
 * @param props Contains the selected workbench tab descriptor.
 * @param props.tab The active tab whose placeholder content should be shown.
 * @returns An accessible tab panel with honest unavailable-feature boundaries.
 */
function WorkbenchTabPanel({ tab }: WorkbenchTabPanelProps) {
  // The panel uses the activity catalog to describe context without loading feature data or
  // introducing separate page navigation outside the workbench frame.
  if (tab.id === 'extraction-center') {
    return (
      <section
        aria-labelledby="workbench-tab-extraction-center"
        className="workbench-tabs__panel"
        id="workbench-tabpanel-extraction-center"
        role="tabpanel"
      >
        <ExtractionCenter />
      </section>
    );
  }

  const activity = getWorkbenchActivity(tab.activityId);
  const ActivityIcon = activity.icon;

  return (
    <section
      aria-labelledby={`workbench-tab-${tab.id}`}
      className="workbench-tabs__panel"
      id={`workbench-tabpanel-${tab.id}`}
      role="tabpanel"
    >
      <div className="workbench-start-summary">
        <Badge variant="secondary">Local shell tab</Badge>
        <h1 id="workbench-work-area-title">{tab.title}</h1>
        <p>{tab.placeholderSummary}</p>
      </div>
      <div className="workbench-start-details" aria-label="Workbench tab placeholder explanation">
        <article className="workbench-start-detail">
          <ActivityIcon aria-hidden="true" size={18} />
          <div>
            <h2>{activity.label} context</h2>
            <p>{activity.description}</p>
            <Badge variant="outline">Placeholder only</Badge>
          </div>
        </article>
        <article className="workbench-start-detail">
          <div aria-hidden="true" className="workbench-start-detail__marker">!</div>
          <div>
            <h2>Feature data is intentionally absent</h2>
            <p>No extraction runs, snapshots, graph data, search results, evidence, or findings are loaded in this shell slice.</p>
            <Badge variant="warning">Later work package</Badge>
          </div>
        </article>
      </div>
    </section>
  );
}
