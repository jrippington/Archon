import { Command, Search } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

/**
 * Renders the non-functional command and search affordance for the shell header.
 *
 * @returns A labelled placeholder that communicates command/search intent without executing queries.
 */
export function CommandSearchPlaceholder() {
  // The disabled button preserves the future control location while making the unavailable
  // state explicit through text, a disabled state, and the Later badge rather than color alone.
  return (
    <div className="workbench-command-search" aria-label="Command and search placeholder">
      <Search aria-hidden="true" size={18} />
      <div className="workbench-command-search__copy">
        <span>Search architecture knowledge and commands</span>
        <span>Functional search and command execution arrive in a later work package.</span>
      </div>
      <Badge variant="warning">Unavailable</Badge>
      <Button aria-disabled="true" disabled type="button" variant="outline">
        <Command aria-hidden="true" size={16} />
        Command palette later
      </Button>
    </div>
  );
}
