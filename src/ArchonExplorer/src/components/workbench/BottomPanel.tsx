import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

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
   * Provides safe explanatory copy without raw diagnostics or fabricated feature data.
   */
  readonly description: string;

  /**
   * Provides the short state label used by the placeholder badge.
   */
  readonly stateLabel: string;
}

/**
 * Lists the safe placeholder sections reserved by the Workbench bottom panel.
 */
const bottomPanelSections: readonly BottomPanelSection[] = [
  {
    title: 'Background Work',
    description: 'No browser-local background work is running. Future packages can report safe queued activity here without exposing raw worker diagnostics.',
    stateLabel: 'Idle',
  },
  {
    title: 'Extraction Runs',
    description: 'Extraction run history is not loaded in this shell slice. Real extraction submission and run detail arrive in later work packages.',
    stateLabel: 'Unavailable',
  },
  {
    title: 'Diagnostics',
    description: 'Diagnostics are limited to safe shell placeholders and never include stack traces, connection strings, environment variables, raw Cypher, Neo4j internals, or driver details.',
    stateLabel: 'Safe placeholder',
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
          <Badge variant="secondary">Contextual panel</Badge>
          <h2>Bottom Panel</h2>
        </div>
        <Button type="button" variant="ghost" size="sm" onClick={onHide}>
          Hide panel
        </Button>
      </div>
      <div className="workbench-bottom-panel__sections">
        {bottomPanelSections.map((section) => (
          <section key={section.title} className="workbench-bottom-panel__section" aria-label={section.title}>
            <div className="workbench-bottom-panel__section-heading">
              <h3>{section.title}</h3>
              <Badge variant="outline">{section.stateLabel}</Badge>
            </div>
            <p>{section.description}</p>
          </section>
        ))}
      </div>
    </aside>
  );
}
