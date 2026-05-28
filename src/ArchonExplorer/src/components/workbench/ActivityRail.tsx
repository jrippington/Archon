import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { workbenchAreas } from './workbenchAreas';

/**
 * Renders the left-side activity rail for the ArchonExplorer shell.
 *
 * @returns A navigation landmark containing placeholder workbench area buttons.
 */
export function ActivityRail() {
  // The buttons are intentionally inert placeholders: they expose the planned information
  // architecture while avoiding route changes or unavailable feature panels in WP001.
  return (
    <nav aria-label="ArchonExplorer workbench areas" className="workbench-activity-rail">
      <div className="workbench-activity-rail__brand" aria-label="ArchonExplorer product mark">
        <span className="workbench-activity-rail__brand-mark" aria-hidden="true">
          AX
        </span>
        <span className="workbench-activity-rail__brand-text">ArchonExplorer</span>
      </div>
      <ul className="workbench-activity-rail__list">
        {workbenchAreas.map((area) => {
          const Icon = area.icon;

          return (
            <li key={area.id}>
              <Button
                aria-current={area.isActive ? 'page' : undefined}
                aria-label={`${area.label}: ${area.description}`}
                className="workbench-activity-rail__item"
                disabled={!area.isActive}
                type="button"
                variant={area.isActive ? 'secondary' : 'ghost'}
              >
                <Icon aria-hidden="true" size={18} />
                <span className="workbench-activity-rail__item-label">{area.label}</span>
                {!area.isActive && <Badge variant="outline">Later</Badge>}
              </Button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
