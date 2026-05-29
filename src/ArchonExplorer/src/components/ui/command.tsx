import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * Describes the root command primitive props used by the local shadcn-compatible command surface.
 */
export interface CommandProps extends ComponentPropsWithoutRef<'div'> {
  /**
   * Contains grouped command content rendered inside the command surface.
   */
  readonly children: ReactNode;
}

/**
 * Renders the root command container used by command-palette surfaces.
 *
 * @param props Contains standard div attributes, optional classes, and command children.
 * @param props.children The command input, list, and command groups rendered inside the root.
 * @param props.className Additional local classes appended to the primitive root.
 * @returns A shadcn-compatible command container without adding a competing UI library.
 */
export function Command({ children, className, ...props }: CommandProps) {
  // The project already maintains local shadcn-compatible primitives, so this lightweight command
  // root provides the same composition seam without introducing cmdk until richer filtering is needed.
  return (
    <div className={cn('ui-command', className)} {...props}>
      {children}
    </div>
  );
}

/**
 * Describes the command input primitive props.
 */
export interface CommandInputProps extends ComponentPropsWithoutRef<'input'> {
  /**
   * Provides the accessible placeholder shown before the user types command filter text.
   */
  readonly placeholder?: string;
}

/**
 * Renders the command palette input field.
 *
 * @param props Contains standard input attributes and optional command input classes.
 * @param props.className Additional local classes appended to the command input.
 * @param props.placeholder Placeholder text that describes command filtering boundaries.
 * @returns A styled command filter input.
 */
export const CommandInput = forwardRef<HTMLInputElement, CommandInputProps>(function CommandInput({ className, placeholder, ...props }, ref) {
  // Filtering remains intentionally simple for this shell slice; the input is present for focus,
  // accessibility, and future command search but does not query architecture data.
  return (
    <input
      ref={ref}
      className={cn('ui-command__input', className)}
      placeholder={placeholder}
      type="text"
      {...props}
    />
  );
});

/**
 * Describes command list primitive props.
 */
export interface CommandListProps extends ComponentPropsWithoutRef<'div'> {
  /**
   * Contains command groups or empty-state content.
   */
  readonly children: ReactNode;
}

/**
 * Renders the scrollable command list region.
 *
 * @param props Contains standard div attributes, optional classes, and command-group children.
 * @param props.children The command choices rendered in the list.
 * @param props.className Additional local classes appended to the command list.
 * @returns A scrollable command list container.
 */
export function CommandList({ children, className, ...props }: CommandListProps) {
  // The role remains on the caller so a dialog can choose listbox, menu, or grouped list semantics
  // as the command model evolves without changing this primitive.
  return (
    <div className={cn('ui-command__list', className)} {...props}>
      {children}
    </div>
  );
}

/**
 * Describes command group primitive props.
 */
export interface CommandGroupProps extends ComponentPropsWithoutRef<'section'> {
  /**
   * Provides the visible command-group heading.
   */
  readonly heading: string;

  /**
   * Contains command items that belong to the group.
   */
  readonly children: ReactNode;
}

/**
 * Renders one grouped section in the command list.
 *
 * @param props Contains the group heading, grouped children, and optional section attributes.
 * @param props.children The command items rendered inside this group.
 * @param props.className Additional local classes appended to the command group.
 * @param props.heading Visible heading used to identify the command group.
 * @returns A grouped command-list section with a stable heading.
 */
export function CommandGroup({ children, className, heading, ...props }: CommandGroupProps) {
  // Each group is rendered as a section to keep the command palette understandable without
  // relying on color or position alone.
  return (
    <section className={cn('ui-command__group', className)} aria-label={heading} {...props}>
      <p className="ui-command__group-heading">{heading}</p>
      <div className="ui-command__group-items">{children}</div>
    </section>
  );
}

/**
 * Describes command item primitive props.
 */
export interface CommandItemProps extends ComponentPropsWithoutRef<'button'> {
  /**
   * Contains the visible command item content.
   */
  readonly children: ReactNode;
}

/**
 * Renders a command choice as a keyboard-reachable button.
 *
 * @param props Contains standard button attributes, command item classes, and item content.
 * @param props.children The command label, description, and optional keyboard hint.
 * @param props.className Additional local classes appended to the command item.
 * @returns A button-based command item.
 */
export function CommandItem({ children, className, type = 'button', ...props }: CommandItemProps) {
  // Native buttons provide accessible keyboard behavior for this slice while leaving room for a
  // future cmdk-backed implementation if richer search and roving focus become necessary.
  return (
    <button className={cn('ui-command__item', className)} type={type} {...props}>
      {children}
    </button>
  );
}
