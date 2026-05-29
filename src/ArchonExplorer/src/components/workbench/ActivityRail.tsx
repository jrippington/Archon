import { Button } from '@/components/ui/button';
import { workbenchActivities, type WorkbenchActivityId } from './workbenchActivities';

/**
 * Describes the activity rail inputs supplied by the local workbench store.
 */
export interface ActivityRailProps {
  /**
   * Identifies the activity currently selected in local shell state.
   */
  readonly activeActivityId: WorkbenchActivityId;

  /**
   * Selects an activity without navigating away from the workbench frame.
   */
  readonly onSelectActivity: (activityId: string) => void;
}

/**
 * Renders the left-side activity rail for the ArchonExplorer shell.
 *
 * @param props Contains the active activity and state transition callback.
 * @param props.activeActivityId The currently selected local workbench activity.
 * @param props.onSelectActivity Callback invoked when a keyboard or pointer action selects an activity.
 * @returns A navigation landmark containing placeholder workbench area buttons.
 */
export function ActivityRail({ activeActivityId, onSelectActivity }: ActivityRailProps) {
  // Activity buttons update local shell state only. They intentionally avoid browser navigation,
  // route changes, data loading, or claims that future workbench features are already complete.
  // The rail is deliberately icon-first so it behaves like IDE activity navigation instead of a
  // page sidebar; accessible labels, title text, and hidden text keep the compact controls usable.
  return (
    <nav aria-label="ArchonExplorer workbench activities" className="workbench-activity-rail" data-scroll-region="activity-rail">
      <div className="workbench-activity-rail__brand" aria-label="ArchonExplorer product mark">
        <span className="workbench-activity-rail__brand-mark" aria-hidden="true">
          AX
        </span>
        <span className="workbench-activity-rail__brand-text">ArchonExplorer</span>
      </div>
      <ul className="workbench-activity-rail__list">
        {workbenchActivities.map((area) => {
          const Icon = area.icon;
          const isActive = area.id === activeActivityId;
          const activityLabel = `${area.label}: ${area.description}`;

          return (
            <li key={area.id}>
              <Button
                aria-current={isActive ? 'page' : undefined}
                aria-label={activityLabel}
                className="workbench-activity-rail__item"
                onClick={() => onSelectActivity(area.id)}
                title={activityLabel}
                type="button"
                variant={isActive ? 'secondary' : 'ghost'}
              >
                {isActive ? <span className="workbench-activity-rail__selected-indicator" aria-hidden="true" /> : null}
                <Icon aria-hidden="true" size={18} />
                <span className="workbench-activity-rail__item-label">{area.label}</span>
                <span className="workbench-activity-rail__tooltip" aria-hidden="true">{area.label}</span>
                {isActive ? <span className="workbench-sr-only">{area.label} selected</span> : null}
              </Button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
