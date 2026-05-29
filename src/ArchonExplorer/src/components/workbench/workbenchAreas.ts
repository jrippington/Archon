import { workbenchActivities, type WorkbenchActivity } from './workbenchActivities';

/**
 * Describes one placeholder area shown in the ArchonExplorer activity rail.
 *
 * This compatibility alias preserves the WP001 export name while WP003 moves the
 * canonical activity metadata into workbenchActivities for state-driven navigation.
 */
export type WorkbenchArea = WorkbenchActivity;

/**
 * Lists the non-functional workbench areas reserved by the shell.
 *
 * @returns The roadmap-aligned activity catalog retained under the older export name.
 */
export const workbenchAreas: readonly WorkbenchArea[] = workbenchActivities;
