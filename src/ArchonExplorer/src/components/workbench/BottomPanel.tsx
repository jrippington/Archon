import { Button } from '@/components/ui/button';
import { ExtractionBackgroundMonitor } from '@/components/extraction-center/ExtractionBackgroundMonitor';

/**
 * Describes the bottom panel inputs supplied by the local workbench shell.
 */
export interface BottomPanelProps {
  /**
   * Hides the bottom panel while preserving safe local layout preferences.
   */
  readonly onHide: () => void;
}

/**
 * Describes one safe placeholder section shown in the bottom panel.
 */
interface BottomPanelSection {
  /**
   * Provides the short section heading displayed in the panel.
   */
  readonly title: string;

  /**
   * Provides terse visible copy without raw diagnostics or fabricated feature data.
   */
  readonly summary: string;

  /**
   * Provides title-based help for the placeholder boundary.
   */
  readonly help: string;
}

/**
 * Lists the safe placeholder sections reserved by the Workbench bottom panel.
 */
const bottomPanelSections: readonly BottomPanelSection[] = [
  {
    title: 'Background Work',
    summary: 'Idle.',
    help: 'No browser-local background work is running. Future packages can report safe queued activity here without exposing raw worker diagnostics.',
  },
  {
    title: 'Diagnostics',
    summary: 'Safe diagnostics only.',
    help: 'Safe diagnostics never include stack traces, connection strings, environment variables, raw Cypher, Neo4j internals, or driver details.',
  },
];

/**
 * Renders the contextual bottom panel reserved for desktop workbench feedback.
 *
 * @param props Contains local shell controls for the bottom panel region.
 * @param props.onHide Callback invoked when the user hides the bottom panel.
 * @returns A bottom-panel region with safe placeholders for later runtime workflows.
 */
export function BottomPanel({ onHide }: BottomPanelProps) {
  // The bottom panel is a shell region, not a feature data surface yet. Every section therefore
  // explains the boundary explicitly and avoids simulated extraction, diagnostics, or API state.
  return (
    <aside className="workbench-bottom-panel" aria-label="Workbench bottom panel">
      <div className="workbench-bottom-panel__header">
        <div>
          <h2>Bottom Panel</h2>
        </div>
        <Button type="button" variant="ghost" size="sm" onClick={onHide}>
          Hide panel
        </Button>
      </div>
      <div className="workbench-bottom-panel__sections">
        <ExtractionBackgroundMonitor />
        {bottomPanelSections.map((section) => (
          <section key={section.title} className="workbench-bottom-panel__section" aria-label={section.title}>
            <div className="workbench-bottom-panel__section-heading">
              <h3>{section.title}</h3>
            </div>
            <p title={section.help}>{section.summary}</p>
          </section>
        ))}
      </div>
    </aside>
  );
}
