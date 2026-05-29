import { Badge } from '@/components/ui/badge';
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
  return (
    <nav aria-label="ArchonExplorer workbench activities" className="workbench-activity-rail">
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

          return (
            <li key={area.id}>
              <Button
                aria-current={isActive ? 'page' : undefined}
                aria-label={`${area.label}: ${area.description}`}
                className="workbench-activity-rail__item"
                onClick={() => onSelectActivity(area.id)}
                type="button"
                variant={isActive ? 'secondary' : 'ghost'}
              >
                <Icon aria-hidden="true" size={18} />
                <span className="workbench-activity-rail__item-label">{area.label}</span>
                {!isActive && <Badge variant="outline">Later</Badge>}
              </Button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
