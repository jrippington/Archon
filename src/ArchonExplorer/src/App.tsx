import { getApiConfiguration } from './config/apiConfiguration';
import { WorkbenchShell } from './components/workbench/WorkbenchShell';

/**
 * Renders the ArchonExplorer application root for the visible workbench foundation shell.
 *
 * The component deliberately avoids feature-specific API calls because WP001 only proves
 * that the browser application can start, read safe configuration state, and render the
 * stable shell regions that later workbench slices will extend.
 *
 * @returns The visible React application root shown by the Vite development server and build output.
 */
function App() {
  // Reading configuration at render time keeps the shell honest about whether an API base
  // URL has been supplied without attempting connectivity or exposing raw settings.
  const apiConfiguration = getApiConfiguration();

  return <WorkbenchShell apiConfiguration={apiConfiguration} />;
}

export default App;
