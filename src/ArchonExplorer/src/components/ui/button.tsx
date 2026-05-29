import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { forwardRef, type ButtonHTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

/**
 * Defines the visual variants for the shadcn-compatible button primitive.
 *
 * The variant map is intentionally small for the foundation shell, but it keeps the same
 * cva-based shape used by shadcn/ui so later generated or hand-authored primitives can
 * share familiar extension points.
 */
const buttonVariants = cva('ui-button', {
  variants: {
    variant: {
      default: 'ui-button--default',
      secondary: 'ui-button--secondary',
      ghost: 'ui-button--ghost',
      outline: 'ui-button--outline',
    },
    size: {
      default: 'ui-button--default-size',
      sm: 'ui-button--sm',
      icon: 'ui-button--icon',
    },
  },
  defaultVariants: {
    variant: 'default',
    size: 'default',
  },
});

/**
 * Describes the button primitive props used by ArchonExplorer shell components.
 */
export interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  /**
   * Renders the button styling onto the child element when composition is needed.
   */
  readonly asChild?: boolean;
}

/**
 * Renders an accessible shadcn-compatible button primitive.
 *
 * @param props Contains standard button attributes, visual variants, optional slotting, and children.
 * @param props.asChild When true, applies the button classes to the child slot instead of a native button.
 * @param props.className Additional classes appended by a caller for local layout needs.
 * @param props.size Selects the height and padding preset for the primitive.
 * @param props.variant Selects the visual treatment for the primitive.
 * @returns A native button or slotted child element with the selected button styling.
 */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button({ asChild = false, className, size, variant, ...props }, ref) {
  // Slot support mirrors shadcn/ui behavior, while the default native button keeps placeholder
  // shell actions keyboard-accessible without introducing menu or command dependencies yet.
  const Comp = asChild ? Slot : 'button';

  return <Comp ref={ref} className={cn(buttonVariants({ className, size, variant }))} {...props} />;
});
