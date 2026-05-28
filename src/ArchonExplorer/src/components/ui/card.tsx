import type { HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

/**
 * Renders the shadcn-compatible card container used by shell panels.
 *
 * @param props Contains section attributes and the panel content.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @returns A bordered panel container with token-driven surface styling.
 */
export function Card({ className, ...props }: HTMLAttributes<HTMLElement>) {
  // A section element gives shell panels a semantic grouping without adding extra ARIA roles.
  return <section className={cn('ui-card', className)} {...props} />;
}

/**
 * Renders the header region of a card.
 *
 * @param props Contains div attributes and heading-support content.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @returns A block that groups titles, descriptions, and card-level actions.
 */
export function CardHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  // Keeping header spacing in the primitive avoids repeating shell-specific title layout rules.
  return <div className={cn('ui-card__header', className)} {...props} />;
}

/**
 * Renders the title text of a card.
 *
 * @param props Contains heading attributes and title content.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @returns A level-three heading styled for panel titles.
 */
export function CardTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  // Cards in the workbench sit below the page h1, so h3 keeps heading order predictable.
  return <h3 className={cn('ui-card__title', className)} {...props} />;
}

/**
 * Renders descriptive card copy below a title.
 *
 * @param props Contains paragraph attributes and descriptive content.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @returns Muted explanatory text for the card header.
 */
export function CardDescription({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  // Descriptions use a paragraph so screen readers receive normal prose rather than metadata.
  return <p className={cn('ui-card__description', className)} {...props} />;
}

/**
 * Renders the main content region of a card.
 *
 * @param props Contains div attributes and card body content.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @returns The card body container.
 */
export function CardContent({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  // Body spacing stays centralized so later cards can preserve a consistent shell rhythm.
  return <div className={cn('ui-card__content', className)} {...props} />;
}
