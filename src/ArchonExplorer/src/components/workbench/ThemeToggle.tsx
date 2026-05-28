import { Moon, Sun } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';

/**
 * Names the DOM attribute that selects the active token set for the shell.
 *
 * The attribute is scoped to the document element so authored CSS variables and later
 * shadcn/ui primitives can resolve the same light or dark theme without extra providers.
 */
const themeAttributeName = 'data-theme';

/**
 * Describes the supported visual themes for the foundation shell.
 */
type ShellTheme = 'dark' | 'light';

/**
 * Applies the selected shell theme to the root document element.
 *
 * @param theme The theme whose token set should be activated for the page.
 */
function applyShellTheme(theme: ShellTheme): void {
  // The root attribute is the single handoff point between React state and tokenized CSS.
  document.documentElement.setAttribute(themeAttributeName, theme);
}

/**
 * Renders a small theme affordance for the foundation shell.
 *
 * @returns A button that toggles between the baseline light and dark token sets.
 */
export function ThemeToggle() {
  // Dark is the default because the current workbench aesthetic is closer to the operator
  // console expected for architecture exploration, while light remains one click away.
  const [theme, setTheme] = useState<ShellTheme>('dark');

  useEffect(() => {
    // Updating the DOM in an effect keeps rendering pure and lets CSS variables react after
    // React has committed the selected theme.
    applyShellTheme(theme);
  }, [theme]);

  /**
   * Switches the shell to the other baseline token set.
   */
  function toggleTheme(): void {
    // The updater form avoids closing over stale state if React batches future interactions.
    setTheme((currentTheme) => (currentTheme === 'dark' ? 'light' : 'dark'));
  }

  return (
    <Button
      aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
      className="workbench-theme-toggle"
      onClick={toggleTheme}
      type="button"
      variant="outline"
    >
      {theme === 'dark' ? <Sun aria-hidden="true" size={16} /> : <Moon aria-hidden="true" size={16} />}
      <span>{theme === 'dark' ? 'Light' : 'Dark'} theme</span>
    </Button>
  );
}
