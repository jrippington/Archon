import {
  ChartNoAxesCombined,
  FolderKanban,
  Search,
  Settings,
  Wrench,
  SquareStack,
  type LucideIcon,
} from 'lucide-react';

/**
 * Describes one local workbench activity registered in the desktop shell.
 */
export interface WorkbenchActivity {
  /**
   * Stores the stable identifier used by state, controls, sidebar context, and tests.
   */
  readonly id: string;

  /**
   * Provides the short label shown in the activity rail and sidebar heading.
   */
  readonly label: string;

  /**
   * Provides an accessible activity summary that does not imply feature completion.
   */
  readonly description: string;

  /**
   * Names the sidebar placeholder heading for the activity's future navigation content.
   */
  readonly sidebarTitle: string;

  /**
   * Provides terse visible sidebar copy for the selected activity.
   */
  readonly sidebarSummary: string;

  /**
   * Explains the current placeholder boundary for the selected activity.
   */
  readonly sidebarDescription: string;

  /**
   * Lists safe placeholder navigation items that reserve space for future feature slices.
   */
  readonly placeholderItems: readonly string[];

  /**
   * Provides the icon component used as a compact visual landmark in the activity rail.
   */
  readonly icon: LucideIcon;
}

/**
 * Lists the roadmap-aligned activities available in the local workbench shell.
 */
export const workbenchActivities = [
  {
    id: 'snapshots',
    label: 'Snapshot Workspace',
    description: 'Primary extraction and snapshot operations workspace.',
    sidebarTitle: 'Snapshot Workspace',
    sidebarSummary: 'Snapshot operations',
    sidebarDescription: 'Snapshot workspace is the primary operational context for explicit extraction requests, update status, run history, and selected run inspection.',
    placeholderItems: ['New Extraction pane', 'Snapshot update status', 'Run history', 'Selected run details'],
    icon: SquareStack,
  },
  {
    id: 'search',
    label: 'Search',
    description: 'Future architecture search and command area.',
    sidebarTitle: 'Search placeholder',
    sidebarSummary: 'Search placeholder',
    sidebarDescription: 'Global architecture search arrives in a later work package. This activity does not run queries or display search results.',
    placeholderItems: ['Future search filters', 'Future saved searches', 'Future result scopes'],
    icon: Search,
  },
  {
    id: 'projects',
    label: 'Projects',
    description: 'Future project catalogue and workspace grouping area.',
    sidebarTitle: 'Projects placeholder',
    sidebarSummary: 'Projects placeholder',
    sidebarDescription: 'Project catalogue navigation arrives in a later work package. This shell does not load repository, solution, or project data.',
    placeholderItems: ['Future project catalogue', 'Future workspace grouping', 'Future project filters'],
    icon: FolderKanban,
  },
  {
    id: 'findings',
    label: 'Findings',
    description: 'Future findings and modernization review area.',
    sidebarTitle: 'Findings placeholder',
    sidebarSummary: 'Findings placeholder',
    sidebarDescription: 'Findings triage and modernization review arrive in later work packages. This activity does not invent findings or recommendations.',
    placeholderItems: ['Future findings inbox', 'Future review queues', 'Future modernization labels'],
    icon: ChartNoAxesCombined,
  },
  {
    id: 'diagnostics',
    label: 'Diagnostics',
    description: 'Future safe setup and runtime diagnostics area.',
    sidebarTitle: 'Diagnostics placeholder',
    sidebarSummary: 'Diagnostics placeholder',
    sidebarDescription: 'Safe diagnostics arrive in a later work package. This shell does not expose raw stack traces, environment variables, connection strings, or driver internals.',
    placeholderItems: ['Future setup status', 'Future safe runtime checks', 'Future troubleshooting prompts'],
    icon: Wrench,
  },
  {
    id: 'settings',
    label: 'Settings',
    description: 'Future local shell preference area.',
    sidebarTitle: 'Settings placeholder',
    sidebarSummary: 'Settings placeholder',
    sidebarDescription: 'Local layout preferences arrive in a later work package. This activity does not persist panel sizes, secrets, diagnostics, or API values.',
    placeholderItems: ['Future layout reset', 'Future shell preferences', 'Future accessibility preferences'],
    icon: Settings,
  },
] as const satisfies readonly WorkbenchActivity[];

/**
 * Identifies a known activity registered in the local workbench shell.
 */
export type WorkbenchActivityId = (typeof workbenchActivities)[number]['id'];

/**
 * Returns the default activity selected when the shell starts or recovers from invalid state.
 *
 * @returns The stable Snapshot workspace activity identifier.
 */
export function getDefaultWorkbenchActivityId(): WorkbenchActivityId {
  // Snapshot workspace is the intended first operational context, so fallback recovery keeps users
  // in the extraction/snapshot workflow rather than returning to a page-style dashboard.
  return 'snapshots';
}

/**
 * Checks whether an arbitrary value matches a registered workbench activity identifier.
 *
 * @param activityId The candidate identifier from user interaction, tests, or future persistence.
 * @returns True when the identifier belongs to the activity catalog; otherwise false.
 */
export function isWorkbenchActivityId(activityId: string): activityId is WorkbenchActivityId {
  // The catalog is the single source of truth for shell activity identifiers, so validation
  // intentionally checks against it instead of duplicating a separate string list.
  return workbenchActivities.some((activity) => activity.id === activityId);
}

/**
 * Resolves an activity by identifier while recovering safely for unknown identifiers.
 *
 * @param activityId The requested activity identifier.
 * @returns The matching activity, or the dashboard activity when the request is unknown.
 */
export function getWorkbenchActivity(activityId: string): WorkbenchActivity {
  // Unknown identifiers can appear through stale UI callbacks or later persisted preferences;
  // rendering the default Snapshot workspace keeps the shell stable and avoids exceptions.
  return workbenchActivities.find((activity) => activity.id === activityId)
    ?? workbenchActivities[0];
}
