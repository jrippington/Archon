import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { CommandPalette } from '@/components/workbench/CommandPalette';
import { NotificationHost } from '@/components/workbench/NotificationHost';
import { getWorkbenchShellCommands } from '@/components/workbench/workbenchCommands';
import { isCommandPaletteShortcut } from '@/hooks/useWorkbenchKeyboardShortcuts';
import { NotificationProvider } from '@/providers/NotificationProvider';
import { defaultWorkbenchTabId, getDefaultWorkbenchState } from '@/state/workbenchStore';

/**
 * Creates the minimal command execution context needed by registry tests.
 *
 * @returns A command context backed by spies so tests can verify shell state actions.
 */
function createCommandContext() {
  // Registry tests should validate command behavior without rendering the whole shell, so each
  // state transition is represented by the same callback shape that WorkbenchShell supplies.
  return {
    state: getDefaultWorkbenchState(),
    selectActivity: vi.fn(),
    openOrFocusTab: vi.fn(),
    selectTab: vi.fn(),
    toggleBottomPanel: vi.fn(),
    showBottomPanel: vi.fn(),
    hideBottomPanel: vi.fn(),
    resetLayoutPreferences: vi.fn(),
    setCommandPaletteVisible: vi.fn(),
    notifyInformation: vi.fn(),
  };
}

/**
 * Verifies shell command registration and safe command execution behavior.
 */
describe('workbench shell commands', () => {
  /**
   * Confirms the registry exposes grouped shell commands without browser navigation.
   */
  it('registers grouped shell commands for activities, panels, tabs, and layout', () => {
    const commands = getWorkbenchShellCommands(createCommandContext());

    expect(commands.some((command) => command.group === 'Activities' && command.label === 'Switch to Snapshots')).toBe(true);
    expect(commands.some((command) => command.group === 'Panels' && command.label === 'Toggle Bottom Panel')).toBe(true);
    expect(commands.some((command) => command.group === 'Tabs' && command.label === 'Open Workbench Start')).toBe(true);
    expect(commands.some((command) => command.group === 'Layout' && command.label === 'Reset Layout Preferences')).toBe(true);
    expect(commands.some((command) => command.group === 'Future Search' && command.isDisabled === true)).toBe(true);
  });

  /**
   * Confirms an activity command updates local workbench state through the supplied action.
   */
  it('executes activity commands through local shell state actions', () => {
    const context = createCommandContext();
    const snapshotsCommand = getWorkbenchShellCommands(context).find((command) => command.id === 'workbench.activity.snapshots');

    snapshotsCommand?.execute();

    expect(context.selectActivity).toHaveBeenCalledWith('snapshots');
    expect(context.setCommandPaletteVisible).not.toHaveBeenCalledWith(false);
  });

  /**
   * Confirms the start-tab command focuses the stable default tab identity.
   */
  it('focuses the default start tab through the tab command', () => {
    const context = createCommandContext();
    const startTabCommand = getWorkbenchShellCommands(context).find((command) => command.id === 'workbench.tab.start');

    startTabCommand?.execute();

    expect(context.selectTab).toHaveBeenCalledWith(defaultWorkbenchTabId);
  });

  /**
   * Confirms disabled future search commands provide safe feedback instead of fabricated results.
   */
  it('keeps future architecture search disabled and safe', () => {
    const context = createCommandContext();
    const searchCommand = getWorkbenchShellCommands(context).find((command) => command.id === 'workbench.search.future');

    searchCommand?.execute();

    expect(context.notifyInformation).toHaveBeenCalledWith({
      operationName: 'Global architecture search is not available yet',
      detail: 'Architecture search arrives in a later work package. No search results, graph data, or architecture artefacts were loaded.',
    });
    expect(context.selectActivity).not.toHaveBeenCalledWith('search-results');
  });
});

/**
 * Verifies command palette markup and notification placement remain safe and accessible.
 */
describe('workbench command palette rendering', () => {
  /**
   * Confirms an open palette renders grouped commands and future-search boundary copy.
   */
  it('renders command groups and placeholder search wording', () => {
    const markup = renderToStaticMarkup(
      <CommandPalette
        isOpen
        commands={getWorkbenchShellCommands(createCommandContext())}
        onClose={() => undefined}
      />,
    );

    expect(markup).toContain('role="dialog"');
    expect(markup).toContain('Workbench command palette');
    expect(markup).toContain('Activities');
    expect(markup).toContain('Toggle Bottom Panel');
    expect(markup).toContain('Global architecture search arrives in a later work package.');
    expect(markup).not.toContain('MATCH (');
    expect(markup).not.toContain('Neo4j driver');
  });

  /**
   * Confirms a closed palette does not render command choices into the shell DOM.
   */
  it('renders nothing when closed', () => {
    const markup = renderToStaticMarkup(
      <CommandPalette
        isOpen={false}
        commands={getWorkbenchShellCommands(createCommandContext())}
        onClose={() => undefined}
      />,
    );

    expect(markup).toBe('');
  });

  /**
   * Confirms the shell notification host uses the existing safe notification runtime.
   */
  it('places a shell notification host without duplicating persistent error surfaces', () => {
    const markup = renderToStaticMarkup(
      <NotificationProvider>
        <NotificationHost />
      </NotificationProvider>,
    );

    expect(markup).toContain('aria-label="Workbench shell notifications"');
    expect(markup).toContain('Shell notifications appear as transient safe messages');
    expect(markup).toContain('aria-label="Application notifications"');
  });
});

/**
 * Verifies keyboard shortcut recognition used by the workbench shell hook.
 */
describe('workbench command keyboard shortcut', () => {
  /**
   * Confirms Ctrl+K and Meta+K open the command palette while unrelated keys do not.
   */
  it('recognizes the documented command palette shortcut', () => {
    expect(isCommandPaletteShortcut({ key: 'k', ctrlKey: true, metaKey: false, shiftKey: false, altKey: false })).toBe(true);
    expect(isCommandPaletteShortcut({ key: 'K', ctrlKey: false, metaKey: true, shiftKey: false, altKey: false })).toBe(true);
    expect(isCommandPaletteShortcut({ key: 'p', ctrlKey: true, metaKey: false, shiftKey: true, altKey: false })).toBe(false);
    expect(isCommandPaletteShortcut({ key: 'k', ctrlKey: true, metaKey: false, shiftKey: false, altKey: true })).toBe(false);
  });
});