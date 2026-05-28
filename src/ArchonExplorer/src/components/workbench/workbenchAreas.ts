import {
  Activity,
  ChartNoAxesCombined,
  FolderKanban,
  LayoutDashboard,
  Search,
  Settings,
  Sparkles,
  SquareStack,
  type LucideIcon,
} from 'lucide-react';

/**
 * Describes one placeholder area shown in the ArchonExplorer activity rail.
 */
export interface WorkbenchArea {
  /**
   * Stable identifier used by React list rendering and accessibility labels.
   */
  readonly id: string;

  /**
   * Short human-readable label shown in the activity rail.
   */
  readonly label: string;

  /**
   * Brief safe explanation of what the placeholder represents.
   */
  readonly description: string;

  /**
   * Icon component used as a visual landmark beside the text label.
   */
  readonly icon: LucideIcon;

  /**
   * Indicates whether the area is the initial visible placeholder in the foundation shell.
   */
  readonly isActive?: boolean;
}

/**
 * Lists the non-functional workbench areas reserved by the WP001 shell.
 *
 * These entries deliberately describe future capability families without wiring navigation,
 * data loading, extraction, search, graph projection, or settings persistence.
 */
export const workbenchAreas: readonly WorkbenchArea[] = [
  {
    id: 'dashboard',
    label: 'Dashboard',
    description: 'Future summary and orientation area.',
    icon: LayoutDashboard,
    isActive: true,
  },
  {
    id: 'extraction-center',
    label: 'Extraction Center',
    description: 'Future extraction run workflow area.',
    icon: Activity,
  },
  {
    id: 'snapshots',
    label: 'Snapshots',
    description: 'Future architecture snapshot management area.',
    icon: SquareStack,
  },
  {
    id: 'search',
    label: 'Search',
    description: 'Future architecture search and command area.',
    icon: Search,
  },
  {
    id: 'projects',
    label: 'Projects',
    description: 'Future project catalogue area.',
    icon: FolderKanban,
  },
  {
    id: 'findings',
    label: 'Findings',
    description: 'Future findings and modernization review area.',
    icon: ChartNoAxesCombined,
  },
  {
    id: 'diagnostics',
    label: 'Diagnostics',
    description: 'Future setup and runtime diagnostics area.',
    icon: Sparkles,
  },
  {
    id: 'settings',
    label: 'Settings',
    description: 'Future local shell preference area.',
    icon: Settings,
  },
];
