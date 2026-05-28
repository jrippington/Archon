import { cva, type VariantProps } from 'class-variance-authority';
import type { HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

/**
 * Defines the badge treatments available to the foundation shell.
 *
 * Badges communicate status text beside existing labels; they must never be the only signal
 * for an important state, so shell copy also states each placeholder state in prose.
 */
const badgeVariants = cva('ui-badge', {
  variants: {
    variant: {
      default: 'ui-badge--default',
      secondary: 'ui-badge--secondary',
      outline: 'ui-badge--outline',
      warning: 'ui-badge--warning',
    },
  },
  defaultVariants: {
    variant: 'default',
  },
});

/**
 * Describes the badge primitive props used for compact shell status labels.
 */
export interface BadgeProps extends HTMLAttributes<HTMLSpanElement>, VariantProps<typeof badgeVariants> {}

/**
 * Renders a compact shadcn-compatible badge.
 *
 * @param props Contains span attributes, the selected visual variant, and the badge content.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @param props.variant Selects the visual treatment for the badge.
 * @returns A non-interactive status label suitable for cards, rail items, and status bar cells.
 */
export function Badge({ className, variant, ...props }: BadgeProps) {
  // The primitive renders a span because badges in this shell annotate existing content rather
  // than acting as buttons, filters, or notification controls.
  return <span className={cn(badgeVariants({ className, variant }))} {...props} />;
}
