import { useEffect, useMemo, useRef, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import type { WorkbenchCommandGroup, WorkbenchShellCommand } from './workbenchCommands';

/**
 * Describes the command palette inputs supplied by the workbench shell.
 */
export interface CommandPaletteProps {
  /**
   * Indicates whether the palette dialog should be visible.
   */
  readonly isOpen: boolean;

  /**
   * Contains shell commands grouped and executed by the palette.
   */
  readonly commands: readonly WorkbenchShellCommand[];

  /**
   * Closes the palette and lets the shell restore focus to the triggering affordance.
   */
  readonly onClose: () => void;
}

/**
 * Defines the visible group ordering used by the command palette.
 */
const commandGroupOrder: readonly WorkbenchCommandGroup[] = ['Activities', 'Panels', 'Tabs', 'Layout', 'Focus', 'Snapshot Workspace', 'Future Search'];

/**
 * Renders the Workbench command palette dialog.
 *
 * @param props Contains palette visibility, shell commands, and the close callback.
 * @param props.commands The available shell commands for the current workbench state.
 * @param props.isOpen Indicates whether the command palette should render.
 * @param props.onClose Callback invoked when the user closes the palette or after a command runs.
 * @returns A keyboard-oriented command palette, or null when closed.
 */
export function CommandPalette({ commands, isOpen, onClose }: CommandPaletteProps) {
  // The palette is intentionally shell-scoped: it filters and executes local shell commands only,
  // while clearly stating that global architecture search is not implemented in this work package.
  const [query, setQuery] = useState('');
  const inputRef = useRef<HTMLInputElement | null>(null);
  const normalizedQuery = query.trim().toLowerCase();
  const visibleCommands = useMemo(() => filterCommands(commands, normalizedQuery), [commands, normalizedQuery]);
  const groupedCommands = useMemo(() => groupCommands(visibleCommands), [visibleCommands]);

  useEffect(() => {
    // Moving focus into the text input when the palette opens gives keyboard users an immediate
    // command entry point without requiring a second Tab press.
    if (isOpen) {
      inputRef.current?.focus();
    }
  }, [isOpen]);

  if (!isOpen) {
    return null;
  }

  return (
    <div className="workbench-command-palette" role="dialog" aria-modal="true" aria-labelledby="workbench-command-palette-title">
      <div className="workbench-command-palette__backdrop" aria-hidden="true" />
      <Command className="workbench-command-palette__panel">
        <div className="workbench-command-palette__header">
          <div>
            <Badge variant="secondary">Shell commands</Badge>
            <h2 id="workbench-command-palette-title">Workbench command palette</h2>
          </div>
          <Button type="button" variant="ghost" size="sm" onClick={onClose}>
            Close
          </Button>
        </div>
        <p className="workbench-command-palette__search-boundary" title="Global architecture search arrives in a later work package; this palette only filters local shell commands.">
          Local shell commands only.
        </p>
        <CommandInput
          ref={inputRef}
          aria-label="Filter workbench shell commands"
          placeholder="Filter shell commands, not architecture data"
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault();
              onClose();
            }
          }}
        />
        <CommandList role="listbox" aria-label="Workbench shell commands">
          {visibleCommands.length === 0 ? (
            <p className="workbench-command-palette__empty" title="The command palette does not query architecture data or backend search routes.">No matching commands.</p>
          ) : commandGroupOrder.map((group) => {
            const groupCommandsForRender = groupedCommands.get(group) ?? [];

            if (groupCommandsForRender.length === 0) {
              return null;
            }

            return (
              <CommandGroup heading={group} key={group}>
                {groupCommandsForRender.map((command) => (
                  <CommandPaletteItem command={command} key={command.id} onExecuted={onClose} />
                ))}
              </CommandGroup>
            );
          })}
        </CommandList>
      </Command>
    </div>
  );
}

/**
 * Describes the rendering inputs for one command palette item.
 */
interface CommandPaletteItemProps {
  /**
   * Provides the command descriptor to render and execute.
   */
  readonly command: WorkbenchShellCommand;

  /**
   * Closes the palette after a command has been handled.
   */
  readonly onExecuted: () => void;
}

/**
 * Renders one command palette item.
 *
 * @param props Contains the command descriptor and close callback.
 * @param props.command The shell command represented by this item.
 * @param props.onExecuted Callback invoked after command execution is handled.
 * @returns A command item button with label, description, hint, and disabled-state explanation.
 */
function CommandPaletteItem({ command, onExecuted }: CommandPaletteItemProps) {
  // Disabled future commands are still executable as explanatory actions so users receive safe
  // notification feedback instead of a silent disabled control or fabricated feature behavior.
  const isVisuallyDisabled = command.isDisabled === true;

  return (
    <CommandItem
      aria-disabled={isVisuallyDisabled}
      onClick={() => {
        command.execute();
        onExecuted();
      }}
    >
      <span className="workbench-command-palette__item-copy">
        <span className="workbench-command-palette__item-label">{command.label}</span>
        {command.description === undefined ? null : <span className="workbench-command-palette__item-description">{command.description}</span>}
        {command.disabledReason === undefined ? null : <span className="workbench-command-palette__item-description">{command.disabledReason}</span>}
      </span>
      {command.keyboardHint === undefined ? null : <Badge variant="outline">{command.keyboardHint}</Badge>}
      {isVisuallyDisabled ? <Badge variant="warning">Unavailable</Badge> : null}
    </CommandItem>
  );
}

/**
 * Filters commands by visible command text.
 *
 * @param commands The full command collection for the current shell state.
 * @param normalizedQuery The lower-cased query text entered by the user.
 * @returns The commands whose label, group, or description matches the query.
 */
function filterCommands(commands: readonly WorkbenchShellCommand[], normalizedQuery: string): readonly WorkbenchShellCommand[] {
  // Filtering is limited to command metadata; it never queries architecture data, snapshots, graph
  // projections, extraction runs, or backend APIs.
  if (normalizedQuery.length === 0) {
    return commands;
  }

  return commands.filter((command) => {
    const searchableText = `${command.label} ${command.group} ${command.description ?? ''}`.toLowerCase();
    return searchableText.includes(normalizedQuery);
  });
}

/**
 * Groups commands by their visible command-palette group.
 *
 * @param commands The filtered command collection to group.
 * @returns A map from command group to commands in source order.
 */
function groupCommands(commands: readonly WorkbenchShellCommand[]): ReadonlyMap<WorkbenchCommandGroup, readonly WorkbenchShellCommand[]> {
  // A map keeps grouping explicit and avoids mutating the command registrations themselves.
  const grouped = new Map<WorkbenchCommandGroup, WorkbenchShellCommand[]>();

  for (const command of commands) {
    const groupCommandsForUpdate = grouped.get(command.group) ?? [];
    groupCommandsForUpdate.push(command);
    grouped.set(command.group, groupCommandsForUpdate);
  }

  return grouped;
}
