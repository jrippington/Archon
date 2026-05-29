import { expect, test } from '@playwright/test';

/**
 * Clears browser-local state before each Extraction Center journey.
 */
test.beforeEach(async ({ page }) => {
  // The workbench reads local storage on initial render, so clearing before the second load keeps
  // the active activity and panel state deterministic for this feature journey.
  await page.goto('/');
  await page.evaluate(() => window.localStorage.clear());
});

/**
 * Validates that the Extraction Center activity opens inside the workbench and renders safe history.
 */
test('opens Extraction Center from the activity rail and renders mocked history safely', async ({ page }) => {
  // The route mock proves the browser journey consumes GET /extractions without a common /api
  // prefix while keeping the test independent of a live ArchonApi instance.
  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runs: [
          {
            runId: 'run-e2e-001',
            status: 'Completed',
            startedUtc: '2026-01-01T00:00:00Z',
            completedUtc: '2026-01-01T00:05:00Z',
            repositoryRootDirectory: 'D:/workspace/Archon',
            solutionCount: 1,
            warningCount: 0,
            errorCount: 0,
            snapshotIdentity: 'snapshot://archon/e2e',
          },
        ],
      }),
    });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await expect(page.getByRole('tab', { name: /Extraction Center/ })).toHaveAttribute('aria-selected', 'true');
  await expect(workRegion.getByRole('heading', { name: 'Extraction Center' })).toBeVisible();
  await expect(workRegion.getByRole('heading', { name: 'Recent extraction history' })).toBeVisible();
  await expect(page.getByRole('row', { name: /run-e2e-001/ })).toContainText('Completed');
  await expect(page.getByRole('row', { name: /run-e2e-001/ })).toContainText('D:/workspace/Archon');
  await expect(page.getByRole('row', { name: /run-e2e-001/ })).toContainText('snapshot://archon/e2e');
  await expect(workRegion).not.toContainText('/api/extractions');
  await expect(workRegion).not.toContainText('System.Exception');
  await expect(workRegion).not.toContainText('Password=');
  await expect(workRegion).not.toContainText('Neo4j driver');
});

/**
 * Validates that a selected active run remains visible in the bottom panel after navigation.
 */
test('keeps a tracked extraction run visible in the bottom panel while navigating away', async ({ page }) => {
  // The active status route stays Running so the background monitor proves long-running work remains
  // visible outside the Extraction Center page without relying on a live scheduler.
  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runs: [
          {
            runId: 'run-e2e-background',
            status: 'Running',
            startedUtc: '2026-01-01T00:00:00Z',
            completedUtc: null,
            repositoryRootDirectory: 'D:/workspace/Archon',
            solutionCount: 1,
            warningCount: 0,
            errorCount: 0,
            snapshotIdentity: null,
          },
        ],
      }),
    });
  });

  await page.route('**/extractions/run-e2e-background', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runId: 'run-e2e-background',
        status: 'Running',
        submittedRequest: {
          repositoryRootDirectory: 'D:/workspace/Archon',
          solutionPaths: ['Archon.sln'],
          branchName: 'main',
          commitSha: 'abc123',
          requestedBy: 'playwright',
          metadataKeys: [],
        },
        startedUtc: '2026-01-01T00:00:00Z',
        completedUtc: null,
        progress: { stage: 'Extraction', message: 'Extraction is running.', percentage: 50, lastUpdatedUtc: '2026-01-01T00:02:00Z' },
        warningCount: 0,
        errorCount: 0,
        timings: [],
        snapshotIdentity: null,
        persistenceDiagnostics: null,
      }),
    });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await workRegion.getByRole('button', { name: 'View details' }).click();
  await expect(workRegion).toContainText('run-e2e-background');
  await page.getByRole('button', { name: /Search:/ }).click();
  await page.getByRole('button', { name: 'Show bottom panel' }).click();

  const bottomPanel = page.getByRole('complementary', { name: 'Workbench bottom panel' });
  await expect(bottomPanel.getByRole('heading', { name: 'Extraction Runs' })).toBeVisible();
  await expect(bottomPanel).toContainText('run-e2e-background');
  await expect(bottomPanel).toContainText('Running');
  await expect(bottomPanel).toContainText('Extraction: Extraction is running. 50% complete.');
  await bottomPanel.getByRole('button', { name: 'Open run' }).click();

  await expect(page.getByRole('tab', { name: /Extraction Center/ })).toHaveAttribute('aria-selected', 'true');
  await expect(workRegion).toContainText('Selected run detail');
  await expect(workRegion).not.toContainText('/api/extractions');
  await expect(workRegion).not.toContainText('Password=');
});

/**
 * Validates Extraction Center commands in the command palette.
 */
test('runs Extraction Center command palette actions safely', async ({ page }) => {
  // Empty history is sufficient because this journey validates command discovery, form focus, and
  // safe no-active-run feedback rather than backend data rendering.
  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({ contentType: 'application/json', status: 200, body: JSON.stringify({ runs: [] }) });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: 'Open command palette' }).click();
  await page.getByLabel('Filter workbench shell commands').fill('Extraction');
  await expect(page.getByRole('button', { name: /^Open Extraction Center/ })).toBeVisible();
  await page.getByRole('button', { name: /^Open Extraction Center/ }).click();
  await expect(page.getByRole('tab', { name: /Extraction Center/ })).toHaveAttribute('aria-selected', 'true');

  await page.getByRole('button', { name: 'Open command palette' }).click();
  await page.getByLabel('Filter workbench shell commands').fill('Focus Extraction Center New Request Form');
  await page.getByRole('button', { name: /^Focus Extraction Center New Request Form/ }).click();
  await expect(page.getByRole('heading', { name: 'Start extraction' })).toBeVisible();

  await page.getByRole('button', { name: 'Open command palette' }).click();
  await page.getByLabel('Filter workbench shell commands').fill('Focus Active Extraction Background Run');
  await page.getByRole('button', { name: /^Focus Active Extraction Background Run/ }).dispatchEvent('click');
  await expect(page.getByRole('status', { name: /Information: No active extraction background run/ })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Workbench editor and bottom panel region' })).not.toContainText('System.Exception');
  await expect(page.getByRole('region', { name: 'Workbench editor and bottom panel region' })).not.toContainText('Password=');
});

/**
 * Validates that selected run status can duplicate safe request values into the form without submitting.
 */
test('duplicates selected extraction request values without submitting them automatically', async ({ page }) => {
  // The POST route only records whether a duplicate action accidentally submits; the status route
  // supplies the full request summary needed to repopulate editable fields safely.
  let postRequestCount = 0;

  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runs: [
          {
            runId: 'run-e2e-duplicate',
            status: 'Completed',
            startedUtc: '2026-01-01T00:00:00Z',
            completedUtc: '2026-01-01T00:05:00Z',
            repositoryRootDirectory: 'D:/workspace/Archon',
            solutionCount: 2,
            warningCount: 0,
            errorCount: 0,
            snapshotIdentity: 'snapshot://archon/e2e-duplicate',
          },
        ],
      }),
    });
  });

  await page.route('**/extractions/run-e2e-duplicate', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runId: 'run-e2e-duplicate',
        status: 'Completed',
        submittedRequest: {
          repositoryRootDirectory: 'D:/workspace/Archon',
          solutionPaths: ['Archon.sln', 'src/ArchonApi/ArchonApi.sln'],
          branchName: 'main',
          commitSha: 'abc123',
          requestedBy: 'playwright',
          metadataKeys: ['source'],
        },
        startedUtc: '2026-01-01T00:00:00Z',
        completedUtc: '2026-01-01T00:05:00Z',
        progress: { stage: 'Completed', message: 'Extraction completed successfully.', percentage: 100, lastUpdatedUtc: '2026-01-01T00:05:00Z' },
        warningCount: 0,
        errorCount: 0,
        timings: [],
        snapshotIdentity: 'snapshot://archon/e2e-duplicate',
        persistenceDiagnostics: null,
      }),
    });
  });

  await page.route('**/extractions', async (route) => {
    if (route.request().method() === 'POST') {
      postRequestCount += 1;
    }

    await route.fallback();
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await workRegion.getByRole('button', { name: 'View details' }).click();
  await expect(workRegion).toContainText('Run follow-up actions');
  await workRegion.getByRole('button', { name: 'Duplicate request' }).click();

  await expect(workRegion.getByLabel('Repository root directory')).toHaveValue('D:/workspace/Archon');
  await expect(workRegion.getByRole('textbox', { name: 'Solution path 1' })).toHaveValue('Archon.sln');
  await expect(workRegion.getByRole('textbox', { name: 'Solution path 2' })).toHaveValue('src/ArchonApi/ArchonApi.sln');
  await expect(workRegion.getByLabel('Branch name')).toHaveValue('main');
  await expect(workRegion.getByLabel('Commit SHA')).toHaveValue('abc123');
  await expect(workRegion.getByLabel('Requested by')).toHaveValue('playwright');
  await expect(workRegion.getByLabel('Metadata')).toHaveValue('');
  await expect(workRegion).toContainText('Metadata values are not exposed by run status and must be re-entered before submitting if needed.');
  expect(postRequestCount).toBe(0);
  await expect(workRegion).not.toContainText('/api/extractions');
  await expect(workRegion).not.toContainText('Password=');
});

/**
 * Validates the produced-snapshot handoff boundary before full snapshot context exists.
 */
test('shows produced-snapshot placeholder action without calling graph or search routes', async ({ page }) => {
  // Route counters fail the test if the placeholder attempts to implement later graph, dashboard,
  // search, lens, visualization, or snapshot deletion workflows in this work item.
  const forbiddenRouteHits: string[] = [];

  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runs: [
          {
            runId: 'run-e2e-snapshot',
            status: 'Completed',
            startedUtc: '2026-01-01T00:00:00Z',
            completedUtc: '2026-01-01T00:05:00Z',
            repositoryRootDirectory: 'D:/workspace/Archon',
            solutionCount: 1,
            warningCount: 0,
            errorCount: 0,
            snapshotIdentity: 'snapshot://archon/e2e-snapshot',
          },
        ],
      }),
    });
  });

  await page.route('**/extractions/run-e2e-snapshot', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runId: 'run-e2e-snapshot',
        status: 'Completed',
        submittedRequest: {
          repositoryRootDirectory: 'D:/workspace/Archon',
          solutionPaths: ['Archon.sln'],
          branchName: 'main',
          commitSha: 'abc123',
          requestedBy: 'playwright',
          metadataKeys: [],
        },
        startedUtc: '2026-01-01T00:00:00Z',
        completedUtc: '2026-01-01T00:05:00Z',
        progress: { stage: 'Completed', message: 'Extraction completed successfully.', percentage: 100, lastUpdatedUtc: '2026-01-01T00:05:00Z' },
        warningCount: 0,
        errorCount: 0,
        timings: [],
        snapshotIdentity: 'snapshot://archon/e2e-snapshot',
        persistenceDiagnostics: null,
      }),
    });
  });

  await page.route(/.*\/(graph|search|dashboard|lenses|visualizations).*/, async (route) => {
    forbiddenRouteHits.push(route.request().url());
    await route.fulfill({ status: 500, body: 'Forbidden in produced-snapshot placeholder test.' });
  });
  await page.route('**/management/snapshots/**', async (route) => {
    forbiddenRouteHits.push(route.request().url());
    await route.fulfill({ status: 500, body: 'Snapshot delete or lifecycle handoff must not run here.' });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await workRegion.getByRole('button', { name: 'View details' }).click();
  await expect(workRegion).toContainText('snapshot://archon/e2e-snapshot');
  await workRegion.getByRole('button', { name: 'Open produced snapshot' }).click();

  await expect(page.getByRole('status', { name: /Information: Produced snapshot handoff/ })).toBeVisible();
  await expect(workRegion).toContainText('Snapshot context is not active yet. WP006 owns opening produced snapshots for dashboards, search, graph views, and lenses.');
  await expect(workRegion).toContainText('This action does not query graph data, dashboard metrics, search, lenses, or visualizations.');
  expect(forbiddenRouteHits).toEqual([]);
  await expect(workRegion).not.toContainText('raw Cypher');
  await expect(workRegion).not.toContainText('Neo4j driver');
});

/**
 * Validates that selecting a mocked run polls status until a completed terminal response is rendered.
 */
test('polls selected extraction status from running to completed safely', async ({ page }) => {
  // The status route returns Running first and Completed second so the browser journey proves the
  // selected-run detail surface updates from active monitoring to terminal output without a live API.
  let statusRequestCount = 0;

  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runs: [
          {
            runId: 'run-e2e-polling',
            status: 'Running',
            startedUtc: '2026-01-01T00:00:00Z',
            completedUtc: null,
            repositoryRootDirectory: 'D:/workspace/Archon',
            solutionCount: 1,
            warningCount: 0,
            errorCount: 0,
            snapshotIdentity: null,
          },
        ],
      }),
    });
  });

  await page.route('**/extractions/run-e2e-polling', async (route) => {
    statusRequestCount += 1;
    const isCompleted = statusRequestCount > 1;
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({
        runId: 'run-e2e-polling',
        status: isCompleted ? 'Completed' : 'Running',
        submittedRequest: {
          repositoryRootDirectory: 'D:/workspace/Archon',
          solutionPaths: ['Archon.sln'],
          branchName: 'main',
          commitSha: 'abc123',
          requestedBy: 'playwright',
          metadataKeys: ['source'],
        },
        startedUtc: '2026-01-01T00:00:00Z',
        completedUtc: isCompleted ? '2026-01-01T00:05:00Z' : null,
        progress: {
          stage: isCompleted ? 'Completed' : 'Extraction',
          message: isCompleted ? 'Extraction completed successfully.' : 'Extraction is running.',
          percentage: isCompleted ? 100 : 50,
          lastUpdatedUtc: isCompleted ? '2026-01-01T00:05:00Z' : '2026-01-01T00:02:00Z',
        },
        warningCount: 0,
        errorCount: 0,
        timings: [
          { stage: 'Total', elapsedMilliseconds: 2500, completedUtc: '2026-01-01T00:05:00Z' },
        ],
        snapshotIdentity: isCompleted ? 'snapshot://archon/e2e-polling' : null,
        persistenceDiagnostics: isCompleted
          ? {
              completed: true,
              timings: [
                { stage: 'Persistence.Commit', elapsedMilliseconds: 1500, completedUtc: '2026-01-01T00:05:00Z' },
              ],
              counts: {
                repositoryCount: 1,
                solutionCount: 1,
                projectCount: 1,
                fileCount: 0,
                nodeCount: 4,
                relationshipCount: 3,
                evidenceCount: 2,
                findingCount: 0,
                warningCount: 0,
                errorCount: 0,
                metricCount: 1,
                generatedSummaryCount: 0,
                metadataEntryCount: null,
                persistenceOperationCount: 2,
                persistenceBatchCount: 1,
                serializedPayloadBytes: null,
              },
            }
          : null,
      }),
    });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await workRegion.getByRole('button', { name: 'View details' }).click();
  await expect(workRegion.getByRole('heading', { name: 'Selected run detail' })).toBeVisible();
  await expect(workRegion).toContainText('run-e2e-polling');
  await expect(workRegion).toContainText('Active monitor');

  await page.waitForResponse((response) => response.url().includes('/extractions/run-e2e-polling') && response.status() === 200);
  await expect(workRegion).toContainText('Terminal status', { timeout: 8_000 });
  await expect(workRegion).toContainText('Extraction completed successfully.');
  await expect(workRegion).toContainText('snapshot://archon/e2e-polling');
  await expect(workRegion).toContainText('Persistence diagnostics');
  await expect(workRegion).toContainText('Persistence.Commit');
  await expect(workRegion).not.toContainText('/api/extractions');
  await expect(workRegion).not.toContainText('System.Exception');
  await expect(workRegion).not.toContainText('Password=');
  await expect(workRegion).not.toContainText('Neo4j driver');
});

/**
 * Validates that an empty Extraction Center history response remains safe and explanatory.
 */
test('renders the empty Extraction Center history state safely', async ({ page }) => {
  // Returning an empty history list exercises the end-to-end empty state without inventing local
  // history data or bypassing the typed frontend request path.
  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({ contentType: 'application/json', status: 200, body: JSON.stringify({ runs: [] }) });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await expect(workRegion).toContainText('No extraction runs are available yet.');
  await expect(workRegion).toContainText('Submit an explicit extraction request above.');
  await expect(workRegion).not.toContainText('/api/extractions');
  await expect(workRegion).not.toContainText('raw Cypher');
});

/**
 * Validates that a user can submit a valid mocked extraction request and see accepted-run feedback.
 */
test('submits a valid mocked extraction request and renders the accepted run safely', async ({ page }) => {
  // The captured request body proves the browser posts to /extractions directly and sends explicit
  // solution paths rather than asking the backend to discover repository solutions.
  let capturedRequest: unknown;

  await page.route('**/health', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({ status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: ['self'], warnings: [] }),
    });
  });

  await page.route('**/ready', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      status: 200,
      body: JSON.stringify({ status: 'Ready', checkedUtc: '2026-01-01T00:00:00Z', dependencies: [{ name: 'graph', status: 'Ready', message: 'Ready.' }], warnings: [] }),
    });
  });

  await page.route('**/extractions?take=20', async (route) => {
    await route.fulfill({ contentType: 'application/json', status: 200, body: JSON.stringify({ runs: [] }) });
  });

  await page.route('**/extractions', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }

    capturedRequest = route.request().postDataJSON();
    await route.fulfill({
      contentType: 'application/json',
      status: 202,
      body: JSON.stringify({
        runId: 'run-e2e-accepted',
        status: 'Queued',
        submittedRequest: {
          repositoryRootDirectory: 'D:/workspace/Archon',
          solutionPaths: ['Archon.sln'],
          branchName: 'main',
          commitSha: 'abc123',
          requestedBy: 'playwright',
          metadataKeys: ['source'],
        },
        startedUtc: '2026-01-01T00:00:00Z',
        completedUtc: null,
        progress: { stage: 'Queued', message: 'The extraction run is queued.', percentage: null, lastUpdatedUtc: '2026-01-01T00:00:00Z' },
        warningCount: 0,
        errorCount: 0,
        timings: [],
        snapshotIdentity: null,
        persistenceDiagnostics: null,
      }),
    });
  });

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Extraction Center:/ }).click();
  const workRegion = page.getByRole('region', { name: 'Workbench editor and bottom panel region' });

  await workRegion.getByLabel('Repository root directory').fill('D:/workspace/Archon');
  await workRegion.getByRole('textbox', { name: 'Solution path 1' }).fill('Archon.sln');
  await workRegion.getByLabel('Branch name').fill('main');
  await workRegion.getByLabel('Commit SHA').fill('abc123');
  await workRegion.getByLabel('Requested by').fill('playwright');
  await workRegion.getByLabel('Metadata').fill('source=e2e');
  await workRegion.getByRole('button', { name: 'Submit extraction' }).click();

  await expect(workRegion.getByRole('heading', { name: 'Accepted run' })).toBeVisible();
  await expect(workRegion).toContainText('run-e2e-accepted');
  await expect(workRegion).toContainText('Queued');
  await expect(workRegion).toContainText('The extraction run is queued.');
  expect(capturedRequest).toEqual({
    repositoryRootDirectory: 'D:/workspace/Archon',
    solutionPaths: ['Archon.sln'],
    branchName: 'main',
    commitSha: 'abc123',
    requestedBy: 'playwright',
    metadata: { source: 'e2e' },
  });
  await expect(workRegion).not.toContainText('/api/extractions');
  await expect(workRegion).not.toContainText('System.Exception');
  await expect(workRegion).not.toContainText('Password=');
  await expect(workRegion).not.toContainText('Neo4j driver');
});
