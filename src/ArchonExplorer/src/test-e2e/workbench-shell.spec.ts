import { expect, test, type Page } from '@playwright/test';

/**
 * Clears browser-local state before each shell journey so persisted layout preferences from a
 * previous run cannot hide the bottom panel or alter the default activity under test.
 */
test.beforeEach(async ({ page }) => {
  // The app reads local storage during initial render, so clearing storage before navigation keeps
  // the journey deterministic without reaching into private React state.
  await page.goto('/');
  await page.evaluate(() => {
    const pendingWikiAnchor = window.location.hash;
    if (!pendingWikiAnchor || pendingWikiAnchor === '#') {
      return;
    }
  });
  await page.evaluate(() => window.localStorage.clear());
  await page.reload();
});

/**
 * Validates the default desktop shell frame and its accessible region names.
 */
test('opens the workbench shell with accessible desktop regions', async ({ page }) => {
  await expect(page.getByRole('navigation', { name: 'ArchonExplorer workbench activities' })).toBeVisible();
  await expect(page.getByRole('complementary', { name: 'Primary workbench sidebar' })).toBeVisible();
  await expect(page.getByRole('main', { name: 'Snapshot Workspace' })).toBeVisible();
  await expect(page.getByRole('tablist', { name: 'Open workbench tabs' })).toBeVisible();
  await expect(page.getByRole('tab', { name: /Snapshot Workspace/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('button', { name: 'Show bottom panel' })).toBeVisible();
  await expect(page.getByRole('contentinfo', { name: 'ArchonExplorer shell status' })).toContainText('current unavailable');
  await expect(page.getByRole('contentinfo', { name: 'ArchonExplorer shell status' })).toContainText('bottom panel hidden');
  await expect(page.getByRole('button', { name: 'Open command palette' })).toBeVisible();
  await expect(page.getByText('Notification host ready for safe shell feedback.')).toBeVisible();
});

/**
 * Validates the compact activity rail contract for icon-only workbench navigation.
 */
test('renders compact icon-only activity navigation with accessible labels and tooltip text', async ({ page }) => {
  const activityRailMetrics = await page.evaluate(() => {
    // Width and label visibility are measured in the browser because the compact rail is a visual
    // workbench contract, while accessible names and title text remain available through markup.
    const rail = document.querySelector<HTMLElement>('.workbench-activity-rail');
    const visibleLabels = Array.from(document.querySelectorAll<HTMLElement>('.workbench-activity-rail__item-label'))
      .filter((label) => window.getComputedStyle(label).display !== 'none');

    return {
      railWidth: rail?.getBoundingClientRect().width ?? 0,
      visibleLabelCount: visibleLabels.length,
    };
  });

  expect(activityRailMetrics.railWidth).toBeLessThanOrEqual(88);
  expect(activityRailMetrics.visibleLabelCount).toBe(0);
  await expect(page.getByRole('button', { name: 'Snapshot Workspace: Primary extraction and snapshot operations workspace.' })).toHaveAttribute('title', 'Snapshot Workspace: Primary extraction and snapshot operations workspace.');
  await expect(page.getByRole('button', { name: 'Snapshot Workspace: Primary extraction and snapshot operations workspace.' })).toContainText('Snapshot Workspace selected');
  await expect(page.locator('.workbench-activity-rail').getByText('Later')).toHaveCount(0);
});

/**
 * Validates that the browser document is a fixed shell host rather than the primary scroll surface.
 */
test('contains scrolling inside named workbench regions instead of the browser document', async ({ page }) => {
  const scrollMetrics = await page.evaluate(() => {
    // Browser-level scroll metrics prove the app root owns the viewport while selected internal
    // regions retain overflow containment for panes, lists, grids, forms, and details surfaces.
    const root = document.querySelector<HTMLElement>('[data-scroll-root="workbench"]');
    const namedRegions = Array.from(document.querySelectorAll<HTMLElement>('[data-scroll-region]'));

    return {
      bodyClientHeight: document.body.clientHeight,
      bodyScrollHeight: document.body.scrollHeight,
      documentClientHeight: document.documentElement.clientHeight,
      documentScrollHeight: document.documentElement.scrollHeight,
      rootClientHeight: root?.clientHeight ?? 0,
      viewportHeight: window.innerHeight,
      regionNames: namedRegions.map((region) => region.dataset.scrollRegion ?? ''),
      regionOverflowValues: namedRegions.map((region) => window.getComputedStyle(region).overflowY),
    };
  });

  expect(scrollMetrics.rootClientHeight).toBe(scrollMetrics.viewportHeight);
  expect(scrollMetrics.bodyClientHeight).toBe(scrollMetrics.viewportHeight);
  expect(scrollMetrics.bodyScrollHeight).toBe(scrollMetrics.viewportHeight);
  expect(scrollMetrics.documentClientHeight).toBe(scrollMetrics.viewportHeight);
  expect(scrollMetrics.documentScrollHeight).toBe(scrollMetrics.viewportHeight);
  expect(scrollMetrics.regionNames).toEqual(expect.arrayContaining(['activity-rail', 'primary-sidebar', 'workspace']));
  expect(scrollMetrics.regionOverflowValues).toEqual(expect.arrayContaining(['auto']));
});

/**
 * Validates keyboard command-palette access, focus movement, and safe search-boundary copy.
 */
test('opens the command palette with the keyboard shortcut and moves focus into filtering', async ({ page }) => {
  await page.getByRole('button', { name: 'Open command palette' }).focus();
  await page.keyboard.press(shortcutForCommandPalette(process.platform));

  const palette = page.getByRole('dialog', { name: 'Workbench command palette' });
  const filterInput = page.getByRole('textbox', { name: 'Filter workbench shell commands' });

  await expect(palette).toBeVisible();
  await expect(filterInput).toBeFocused();
  await expect(palette).toContainText('Local shell commands only.');
  await expect(palette.getByText('Local shell commands only.')).toHaveAttribute('title', 'Global architecture search arrives in a later work package; this palette only filters local shell commands.');
  await filterInput.fill('bottom');
  await expect(page.getByRole('button', { name: /Toggle Bottom Panel/ })).toBeVisible();
});

/**
 * Validates activity navigation without leaving the shell frame.
 */
test('switches activities and updates contextual sidebar and status text', async ({ page }) => {
  await page.getByRole('button', { name: 'Search: Future architecture search and command area.' }).click();

  await expect(page.getByRole('complementary', { name: 'Primary workbench sidebar' })).toContainText('Search');
  await expect(page.getByRole('complementary', { name: 'Primary workbench sidebar' })).toContainText('Search placeholder');
  await expect(page.getByRole('contentinfo', { name: 'ArchonExplorer shell status' })).toContainText('Search activity selected; no item selected');
  await expect(page.getByRole('main', { name: 'Snapshot Workspace' })).toBeVisible();
});

/**
 * Validates the bottom-panel user action, text-based state reporting, and command restoration path.
 */
test('toggles the bottom panel through visible controls and command palette commands', async ({ page }) => {
  await page.getByRole('button', { name: 'Show bottom panel' }).click();
  await expect(page.getByRole('complementary', { name: 'Workbench bottom panel' })).toBeVisible();

  await page.getByRole('button', { name: 'Hide bottom panel' }).click();
  await expect(page.getByRole('complementary', { name: 'Workbench bottom panel' })).toBeHidden();
  await expect(page.getByRole('contentinfo', { name: 'ArchonExplorer shell status' })).toContainText('bottom panel hidden');

  await runShellCommand(page, 'Show bottom panel');

  await expect(page.getByRole('complementary', { name: 'Workbench bottom panel' })).toBeVisible();
  await expect(page.getByRole('contentinfo', { name: 'ArchonExplorer shell status' })).toContainText('bottom panel visible');
});

/**
 * Validates keyboard focus visibility and accessible labels for major shell controls.
 */
test('keeps major shell controls keyboard reachable with visible focus indicators', async ({ page }) => {
  const commandTrigger = page.getByRole('button', { name: 'Open command palette' });

  await page.getByRole('button', { name: 'Show bottom panel' }).click();
  await commandTrigger.focus();
  await expect(commandTrigger).toBeFocused();
  await expect(commandTrigger).toHaveCSS('outline-color', /rgb\(/);

  await expect(page.getByRole('separator', { name: 'Resize primary sidebar' })).toHaveAttribute('aria-valuenow', /\d+/);
  await expect(page.getByRole('separator', { name: 'Resize bottom panel' })).toHaveAttribute('aria-valuenow', /\d+/);
  await expect(page.getByRole('button', { name: 'Snapshot Workspace: Primary extraction and snapshot operations workspace.' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('button', { name: 'Snapshot Workspace: Primary extraction and snapshot operations workspace.' })).toContainText('Snapshot Workspace selected');
  await expect(page.getByRole('button', { name: 'Hide panel' })).toBeVisible();
});

/**
 * Selects a shell command from the keyboard-opened palette.
 *
 * @param page The Playwright page containing the running ArchonExplorer shell.
 * @param commandName The accessible command item label to activate.
 */
async function runShellCommand(page: Page, commandName: string): Promise<void> {
  // Commands are native buttons inside the palette, so clicking by role exercises the same
  // accessible surface that keyboard users reach after opening the palette.
  await page.getByRole('button', { name: 'Open command palette' }).focus();
  await page.keyboard.press(shortcutForCommandPalette(process.platform));
  await page.getByRole('button', { name: new RegExp(commandName) }).press('Enter');
}

/**
 * Resolves the platform-appropriate command-palette shortcut for the current test runner.
 *
 * @param platform The Node platform string reported by the Playwright worker process.
 * @returns The shortcut sequence understood by Playwright keyboard automation.
 */
function shortcutForCommandPalette(platform: NodeJS.Platform): string {
  // The shell supports Meta+K on macOS and Ctrl+K everywhere else, matching the documented header
  // hint while keeping the browser test portable across contributor machines.
  return platform === 'darwin' ? 'Meta+K' : 'Control+K';
}
