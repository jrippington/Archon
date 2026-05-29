import { getWorkbenchActivity, type WorkbenchActivityId } from './workbenchActivities';

/**
 * Describes the contextual sidebar inputs supplied by the shell state.
 */
export interface PrimarySidebarProps {
  /**
   * Identifies the selected activity whose contextual placeholder should be rendered.
   */
  readonly activeActivityId: WorkbenchActivityId;
}

/**
 * Renders the activity-specific primary sidebar placeholder.
 *
 * @param props Contains the active activity selected in the local shell state.
 * @param props.activeActivityId The activity identifier used to resolve contextual sidebar copy.
 * @returns A primary sidebar region with safe roadmap-aligned placeholder content.
 */
export function PrimarySidebar({ activeActivityId }: PrimarySidebarProps) {
  // The sidebar is contextual and state-driven, but every entry remains an honest placeholder.
  // It must not query API data, fabricate counts, or imply unavailable feature workflows exist.
  const activity = getWorkbenchActivity(activeActivityId);

  return (
    <aside className="workbench-primary-sidebar" aria-label="Primary workbench sidebar" data-scroll-region="primary-sidebar">
      <div className="workbench-primary-sidebar__header">
        <h2>{activity.sidebarTitle}</h2>
        <p title={activity.sidebarDescription}>{activity.sidebarSummary}</p>
      </div>
      <div className="workbench-primary-sidebar__body" aria-label={`${activity.label} placeholder navigation`}>
        <p className="workbench-primary-sidebar__boundary" title="Future activity-specific navigation remains unavailable until its owning work package implements it.">
          Navigation placeholders.
        </p>
        <ul className="workbench-primary-sidebar__list">
          {activity.placeholderItems.map((item) => (
            <li key={item}>
              <span>{item}</span>
            </li>
          ))}
        </ul>
      </div>
    </aside>
  );
}
