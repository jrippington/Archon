import { useEffect, type RefObject } from 'react';

/**
 * Describes the keyboard event fields needed to recognize shell shortcuts.
 */
export interface WorkbenchKeyboardShortcutEvent {
  /**
   * Provides the key value reported by the browser or test event.
   */
  readonly key: string;

  /**
   * Indicates whether the Control modifier was held.
   */
  readonly ctrlKey: boolean;

  /**
   * Indicates whether the platform Meta modifier was held.
   */
  readonly metaKey: boolean;

  /**
   * Indicates whether the Shift modifier was held.
   */
  readonly shiftKey: boolean;

  /**
   * Indicates whether the Alt modifier was held.
   */
  readonly altKey: boolean;
}

/**
 * Describes the inputs required by the Workbench keyboard shortcut hook.
 */
export interface WorkbenchKeyboardShortcutOptions {
  /**
   * Indicates whether the command palette is currently visible.
   */
  readonly isCommandPaletteVisible: boolean;

  /**
   * Opens or closes the command palette in local shell state.
   */
  readonly setCommandPaletteVisible: (isVisible: boolean) => void;

  /**
   * References the on-screen command affordance so focus can return after palette dismissal.
   */
  readonly triggerRef: RefObject<HTMLButtonElement | null>;
}

/**
 * Checks whether a keyboard event is the documented command-palette shortcut.
 *
 * @param event The keyboard event or test event shape to inspect.
 * @returns True when the event represents Ctrl+K or Meta+K without Alt or Shift modifiers.
 */
export function isCommandPaletteShortcut(event: WorkbenchKeyboardShortcutEvent): boolean {
  // Ctrl+K is a common command-palette shortcut and Meta+K supports macOS keyboards. Alt and
  // Shift are excluded so browser or assistive-technology combinations are not captured broadly.
  return event.key.toLowerCase() === 'k'
    && (event.ctrlKey || event.metaKey)
    && !event.shiftKey
    && !event.altKey;
}

/**
 * Registers Workbench shell keyboard shortcuts and focus return behavior.
 *
 * @param options The palette visibility state, setter, and trigger focus target.
 */
export function useWorkbenchKeyboardShortcuts(options: WorkbenchKeyboardShortcutOptions): void {
  // The listener is global to the document because the command palette is a shell-level affordance
  // that should open from any workbench region without changing browser location.
  useEffect(() => {
    /**
     * Handles keydown events for shell shortcuts.
     *
     * @param event The browser keyboard event emitted by the document.
     */
    function handleKeyDown(event: KeyboardEvent): void {
      // Preventing default only for the exact shell shortcut keeps normal typing, browser search,
      // and unrelated accessibility shortcuts available to the user.
      if (isCommandPaletteShortcut(event)) {
        event.preventDefault();
        options.setCommandPaletteVisible(true);
      }
    }

    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [options]);

  useEffect(() => {
    // When the palette closes, return focus to the visible affordance if the document still owns
    // focus. This gives keyboard users a stable recovery point without forcing focus during SSR.
    if (!options.isCommandPaletteVisible) {
      options.triggerRef.current?.focus();
    }
  }, [options.isCommandPaletteVisible, options.triggerRef]);
}
