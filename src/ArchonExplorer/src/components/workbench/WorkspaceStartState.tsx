import { Boxes, CircleHelp, Workflow } from 'lucide-react';

/**
 * Defines one explanatory start-state item for the foundation workspace.
 */
interface StartStateItem {
  /**
   * Stable identifier used by React list rendering.
   */
  readonly id: string;

  /**
   * Short heading shown for the item.
   */
  readonly title: string;

  /**
   * Human-readable help text exposed through title attributes.
   */
  readonly description: string;

  /**
   * Icon shown as a plain leading marker for fast scanning without card styling.
   */
  readonly icon: typeof Boxes;
}

/**
 * Lists the shell facts that should be visible in the empty workspace state.
 */
const startStateItems: readonly StartStateItem[] = [
  {
    id: 'foundation-ready',
    title: 'Workbench frame is ready',
    description: 'The shell reserves durable regions for navigation, commands, workspace content, and status without replacing the Vite frontend foundation.',
    icon: Boxes,
  },
  {
    id: 'feature-boundary',
    title: 'Operational features are intentionally absent',
    description: 'Extraction runs, snapshots, graph visualisation, lenses, evidence inspection, findings triage, and real notifications are not implemented in this slice.',
    icon: CircleHelp,
  },
  {
    id: 'extension-path',
    title: 'Later work packages can extend the seams',
    description: 'Future slices can attach real routes, server-state queries, command handling, and investigation panels without rebuilding the app frame.',
    icon: Workflow,
  },
];

/**
 * Renders the legacy main workspace start state for the foundation shell.
 *
 * The WP003 tabbed work area now hosts the default start content, but this component remains
 * available for older tests or transitional callers that still need the foundation copy.
 *
 * @returns A non-interactive workspace panel that explains what exists and what remains pending.
 */
export function WorkspaceStartState() {
  // The start state uses plain sections and direct prose so unavailable capability
  // boundaries read like an IDE workspace rather than a marketing page.
  return (
    <main className="workbench-workspace" aria-labelledby="workbench-start-title">
      <section className="workbench-start-summary">
        <h1 id="workbench-start-title">ArchonExplorer</h1>
        <p title="The desktop-style shell reserves navigation, commands, workspace content, and status without performing extraction, snapshot, search, graph, lens, evidence, finding, or notification work.">Foundation shell.</p>
      </section>
      <section className="workbench-start-details" aria-label="Foundation shell explanation">
        {startStateItems.map((item) => {
          const Icon = item.icon;

          return (
            <article className="workbench-start-detail" key={item.id}>
              <Icon aria-hidden="true" size={18} />
              <div>
                <h2>{item.title}</h2>
                <p title={item.description}>Placeholder only.</p>
              </div>
            </article>
          );
        })}
      </section>
    </main>
  );
}
