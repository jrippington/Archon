import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Combines conditional CSS class values using the same convention as shadcn/ui primitives.
 *
 * The helper keeps primitive components small by centralizing `clsx` condition handling and
 * `tailwind-merge` conflict resolution, even when the current shell uses mostly authored CSS.
 *
 * @param inputs The class values, arrays, and conditional maps that should be composed.
 * @returns A single class-name string with duplicate Tailwind-style conflicts resolved.
 */
export function cn(...inputs: ClassValue[]): string {
  // shadcn/ui primitives conventionally call this helper so callers can override classes
  // without manually reasoning about duplicate spacing, color, or layout utility names.
  return twMerge(clsx(inputs));
}
