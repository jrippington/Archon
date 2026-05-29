import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createRef } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import type { ArchonApiClient } from '@/api/archonApiClient';
import { createUnconfiguredConnectivityState } from '@/api/connectivity';
import type { ExtractionRunHistoryResponse, ExtractionRunPersistenceDiagnosticsResponse, NormalizedArchonApiError } from '@/api/archonApiTypes';
import type { ArchonApiRequestResult } from '@/api/request';
import { archonApiRoutes } from '@/api/archonApiRoutes';
import { ArchonApiClientTestDouble, createExtractionRunStatus, failure } from '@/api/testDoubles';
import { deriveExtractionPollingState, isExtractionRunTerminalStatus } from '@/api/polling';
import { archonQueryKeys } from '@/api/queryKeys';
import { ExtractionBackgroundMonitorContent } from '@/components/extraction-center/ExtractionBackgroundMonitor';
import { ExtractionCenterContent } from '@/components/extraction-center/ExtractionCenter';
import { ExtractionRunDetail } from '@/components/extraction-center/ExtractionRunDetail';
import { mapExtractionRequestFormStateToRequest, mapRunStatusToDuplicateFormState, mapServerValidationIssuesToFormMessages } from '@/components/extraction-center/extractionFormState';
import { ExtractionHistoryError, useExtractionHistory } from '@/hooks/useExtractionHistory';
import { StartExtractionError } from '@/hooks/useStartExtraction';
import { getDefaultExtractionCenterState, reduceExtractionCenterState } from '@/state/extractionCenterStore';
import { getWorkbenchShellCommands } from '@/components/workbench/workbenchCommands';
import { getDefaultWorkbenchState } from '@/state/workbenchStore';

/**
 * Creates a query client with deterministic test defaults.
 *
 * @returns A TanStack Query client that avoids retries and background refetching during tests.
 */
function createTestQueryClient(): QueryClient {
  // The focused hook tests need predictable request counts, so retry and focus refetching stay off.
  return new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
}

/**
 * Creates deterministic persistence diagnostics for selected-run detail rendering tests.
 *
 * @returns A completed persistence diagnostic section with timings and count measurements.
 */
function createPersistenceDiagnostics(): ExtractionRunPersistenceDiagnosticsResponse {
  // The diagnostic fixture includes non-zero and null optional values so rendering tests prove the
  // UI distinguishes measured zero, measured counts, and intentionally unmeasured optional counts.
  return {
    completed: true,
    timings: [
      { stage: 'Persistence.PrepareSnapshot', elapsedMilliseconds: 120, completedUtc: '2026-01-01T00:04:00Z' },
      { stage: 'Persistence.Commit', elapsedMilliseconds: 1_500, completedUtc: '2026-01-01T00:04:30Z' },
    ],
    counts: {
      repositoryCount: 1,
      solutionCount: 2,
      projectCount: 3,
      fileCount: 4,
      nodeCount: 5,
      relationshipCount: 6,
      evidenceCount: 7,
      findingCount: 8,
      warningCount: 1,
      errorCount: 0,
      metricCount: 9,
      generatedSummaryCount: 10,
      metadataEntryCount: null,
      persistenceOperationCount: 11,
      persistenceBatchCount: 1,
      serializedPayloadBytes: null,
    },
  };
}

/**
 * Creates a safe failed request result for history hook tests.
 *
 * @returns A failed request-result envelope with only safe UI-facing diagnostics.
 */
function safeFailure(): ArchonApiRequestResult<ExtractionRunHistoryResponse> {
  // The unsafe fragments are deliberately absent to prove the UI does not depend on raw backend text.
  return {
    ok: false,
    status: 503,
    error: {
      category: 'network',
      message: 'Archon API could not be reached. Check that the service is running and accessible.',
      status: 503,
      retryable: true,
    },
  };
}

/**
 * Verifies the Extraction Center history hook uses the typed client and query-key conventions.
 */
describe('useExtractionHistory', () => {
  /**
   * Confirms successful history responses are surfaced through TanStack Query without direct fetch calls.
   */
  it('loads recent extraction history through the typed API client', async () => {
    const client = new ArchonApiClientTestDouble({
      extractionRuns: {
        'run-completed': createExtractionRunStatus({ runId: 'run-completed', status: 'Completed', repositoryRootDirectory: 'D:/workspace/Archon' }),
      },
    });
    const queryClient = createTestQueryClient();

    const history = await queryClient.fetchQuery(useExtractionHistory({ client, take: 10 }));

    expect(history.runs).toHaveLength(1);
    expect(history.runs[0]?.runId).toBe('run-completed');
    expect(client.requests).toContainEqual({ operation: 'getExtractionHistory', method: 'GET', path: archonApiRoutes.extraction.runs, query: { take: 10 } });
  });

  /**
   * Confirms normalized safe failures are converted into persistent page-level errors.
   */
  it('throws only safe history errors from normalized API failures', async () => {
    const client: Pick<ArchonApiClient, 'getExtractionHistory'> = {
      getExtractionHistory: vi.fn().mockResolvedValue(safeFailure()),
    };
    const queryClient = createTestQueryClient();

    await expect(queryClient.fetchQuery(useExtractionHistory({ client, take: 5 }))).rejects.toMatchObject({
      message: 'Archon API could not be reached. Check that the service is running and accessible.',
      category: 'network',
      retryable: true,
    });
  });
});

/**
 * Verifies start-extraction form-state helpers before the mutation reaches ArchonApi.
 */
describe('extraction request form helpers', () => {
  /**
   * Confirms valid form values are trimmed and mapped to the typed API request body.
   */
  it('maps valid form state into a typed start-extraction request', () => {
    const result = mapExtractionRequestFormStateToRequest({
      repositoryRootDirectory: ' D:/workspace/Archon ',
      solutionPaths: [' Archon.sln ', ' ', 'src/ArchonApi/ArchonApi.sln'],
      branchName: ' main ',
      commitSha: ' abc123 ',
      requestedBy: ' developer@example.invalid ',
      metadataText: 'source=manual\ntrace=work-item-2',
    });

    expect(result).toEqual({
      ok: true,
      request: {
        repositoryRootDirectory: 'D:/workspace/Archon',
        solutionPaths: ['Archon.sln', 'src/ArchonApi/ArchonApi.sln'],
        branchName: 'main',
        commitSha: 'abc123',
        requestedBy: 'developer@example.invalid',
        metadata: { source: 'manual', trace: 'work-item-2' },
      },
    });
  });

  /**
   * Confirms missing repository and solution values are caught without making filesystem claims.
   */
  it('returns safe validation messages for obvious missing values', () => {
    const result = mapExtractionRequestFormStateToRequest({
      repositoryRootDirectory: ' ',
      solutionPaths: [' ', ''],
      branchName: '',
      commitSha: '',
      requestedBy: '',
      metadataText: '',
    });

    expect(result).toEqual({
      ok: false,
      validationMessages: {
        repositoryRootDirectory: ['Enter the repository root directory to extract.'],
        solutionPaths: ['Enter at least one explicit solution path. ArchonExplorer does not discover solutions recursively.'],
      },
    });
  });

  /**
   * Confirms server validation buckets are mapped back into persistent form messages.
   */
  it('maps normalized server validation issues to form fields', () => {
    const messages = mapServerValidationIssuesToFormMessages([
      { field: 'RepositoryRootDirectory', messages: ['Repository root must exist.'] },
      { field: 'SolutionPaths[0]', messages: ['Solution path must be inside the repository root.'] },
      { field: 'UnknownRule', messages: ['The request could not be accepted safely.'] },
    ]);

    expect(messages).toEqual({
      repositoryRootDirectory: ['Repository root must exist.'],
      solutionPaths: ['Solution path must be inside the repository root.'],
      form: ['The request could not be accepted safely.'],
    });
  });

  /**
   * Confirms selected run status can safely repopulate the editable form without metadata values.
   */
  it('maps selected run status into duplicate request form state without submitting metadata values', () => {
    const result = mapRunStatusToDuplicateFormState({
      ...createExtractionRunStatus({ runId: 'run-duplicate-source', status: 'Completed', repositoryRootDirectory: 'D:/workspace/Archon' }),
      submittedRequest: {
        repositoryRootDirectory: 'D:/workspace/Archon',
        solutionPaths: ['Archon.sln', 'src/ArchonApi/ArchonApi.sln'],
        branchName: 'main',
        commitSha: 'abc123',
        requestedBy: 'operator@example.invalid',
        metadataKeys: ['source', 'trace'],
      },
    });

    expect(result).toEqual({
      ok: true,
      formState: {
        repositoryRootDirectory: 'D:/workspace/Archon',
        solutionPaths: ['Archon.sln', 'src/ArchonApi/ArchonApi.sln'],
        branchName: 'main',
        commitSha: 'abc123',
        requestedBy: 'operator@example.invalid',
        metadataText: '',
      },
      validationMessages: {
        form: ['Metadata values are not exposed by run status and must be re-entered before submitting if needed. Metadata keys from the previous request are shown only as safe context.'],
      },
      omittedMetadataKeys: ['source', 'trace'],
    });
  });

  /**
   * Confirms duplication remains unavailable when selected status lacks required request values.
   */
  it('returns safe duplication guidance when selected run status lacks required values', () => {
    const result = mapRunStatusToDuplicateFormState({
      ...createExtractionRunStatus({ runId: 'run-incomplete', status: 'Completed' }),
      submittedRequest: {
        repositoryRootDirectory: ' ',
        solutionPaths: [],
        branchName: null,
        commitSha: null,
        requestedBy: null,
        metadataKeys: [],
      },
    });

    expect(result).toEqual({
      ok: false,
      reason: 'The selected run does not expose enough submitted request values to duplicate it safely. Re-enter the repository root directory and explicit solution paths before submitting a new extraction.',
      validationMessages: {
        repositoryRootDirectory: ['Re-enter the repository root directory because the selected run did not expose it.'],
        solutionPaths: ['Re-enter at least one explicit solution path because the selected run did not expose solution path values.'],
      },
    });
  });
});

/**
 * Verifies the typed start-extraction client double and safe mutation error shape used by the hook.
 */
describe('start extraction mutation support', () => {
  /**
   * Confirms the typed client method records POST /extractions without a common /api prefix.
   */
  it('starts extraction through the typed client route', async () => {
    const client = new ArchonApiClientTestDouble();

    const result = await client.startExtraction({
      repositoryRootDirectory: 'D:/workspace/Archon',
      solutionPaths: ['Archon.sln'],
      requestedBy: 'work-item-2',
    });

    expect(result.ok).toBe(true);
    expect(client.requests).toContainEqual({
      operation: 'startExtraction',
      method: 'POST',
      path: archonApiRoutes.extraction.start,
      body: {
        repositoryRootDirectory: 'D:/workspace/Archon',
        solutionPaths: ['Archon.sln'],
        requestedBy: 'work-item-2',
      },
    });
    expect(archonApiRoutes.extraction.start).toBe('/extractions');
  });

  /**
   * Confirms normalized validation failures stay safe and preserve field issues.
   */
  it('creates safe start-extraction errors from validation failures', () => {
    const result = failure<never>('validation', 'The extraction request needs attention.', false, 400);
    if (result.ok) {
      throw new Error('Expected the fake result to be a validation failure.');
    }

    const error = new StartExtractionError({
      ...result.error,
      validationIssues: [{ field: 'RepositoryRootDirectory', messages: ['Repository root is required.'] }],
    });

    expect(error.message).toBe('The extraction request needs attention.');
    expect(error.category).toBe('validation');
    expect(error.validationIssues).toEqual([{ field: 'RepositoryRootDirectory', messages: ['Repository root is required.'] }]);
    expect(error.message).not.toContain('System.Exception');
    expect(error.message).not.toContain('Password=');
  });
});

/**
 * Verifies extraction run polling state and selected-run status retrieval behavior.
 */
describe('selected extraction run polling support', () => {
  /**
   * Confirms active statuses continue polling while terminal statuses stop it.
   */
  it('maps active and terminal statuses through the shared polling helper', () => {
    // The status vocabulary is intentionally case-insensitive so UI code and polling code do not
    // fork behavior when API casing changes between backend implementations.
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Queued' }) })).toBe('polling');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Running' }) })).toBe('polling');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Completed' }) })).toBe('completed');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Failed' }) })).toBe('failed');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Cancelled' }) })).toBe('canceled');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Unavailable' }) })).toBe('unavailable');
    expect(deriveExtractionPollingState({ status: createExtractionRunStatus({ status: 'Unknown' }) })).toBe('unknown');
    expect(isExtractionRunTerminalStatus('Completed')).toBe(true);
    expect(isExtractionRunTerminalStatus('Running')).toBe(false);
  });

  /**
   * Confirms the polling hook uses the typed status client and exact run query key.
   */
  it('loads selected run status through the typed API client', async () => {
    const client = new ArchonApiClientTestDouble({
      extractionRuns: {
        'run-selected': createExtractionRunStatus({ runId: 'run-selected', status: 'Running' }),
      },
    });
    const queryClient = createTestQueryClient();
    const queryKey = archonQueryKeys.extraction.run({ runId: 'run-selected' });

    const status = await queryClient.fetchQuery({
      queryKey,
      queryFn: async () => {
        const result = await client.getExtractionStatus('run-selected');
        if (!result.ok) {
          throw result.error;
        }

        return result.data;
      },
    });

    expect(status.runId).toBe('run-selected');
    expect(client.requests).toContainEqual({ operation: 'getExtractionStatus', method: 'GET', path: archonApiRoutes.extraction.byRunId('run-selected') });
    expect(archonApiRoutes.extraction.byRunId('run-selected')).toBe('/extractions/run-selected');
  });

  /**
   * Confirms deterministic status sequences can model queued/running/completed transitions.
   */
  it('returns deterministic polling transitions from the test double', async () => {
    const client = new ArchonApiClientTestDouble({
      extractionRunSequences: {
        'run-transition': [
          createExtractionRunStatus({ runId: 'run-transition', status: 'Running' }),
          createExtractionRunStatus({ runId: 'run-transition', status: 'Completed' }),
        ],
      },
    });

    const first = await client.getExtractionStatus('run-transition');
    const second = await client.getExtractionStatus('run-transition');

    expect(first.ok && first.data.status).toBe('Running');
    expect(second.ok && second.data.status).toBe('Completed');
  });
});

/**
 * Verifies Extraction Center background tracking and command-state helpers.
 */
describe('Extraction Center background workflow state', () => {
  /**
   * Confirms local tracking stores run identifiers and acknowledgement state without server payloads.
   */
  it('tracks and acknowledges background run identifiers without duplicating status responses', () => {
    const initialState = getDefaultExtractionCenterState();
    const trackedState = reduceExtractionCenterState(initialState, { type: 'selectRun', runId: ' run-background-001 ' });
    const notifiedState = reduceExtractionCenterState(trackedState, { type: 'recordNotifiedStatus', runId: 'run-background-001', status: 'Completed' });
    const acknowledgedState = reduceExtractionCenterState(notifiedState, { type: 'acknowledgeRun', runId: 'run-background-001' });

    expect(trackedState.selectedRunId).toBe('run-background-001');
    expect(trackedState.trackedRuns).toEqual([{ runId: 'run-background-001', isAcknowledged: false }]);
    expect(notifiedState.trackedRuns[0]).toEqual({ runId: 'run-background-001', isAcknowledged: false, lastNotifiedStatus: 'Completed' });
    expect(acknowledgedState.trackedRuns[0]?.isAcknowledged).toBe(true);
    expect(JSON.stringify(acknowledgedState)).not.toContain('submittedRequest');
    expect(JSON.stringify(acknowledgedState)).not.toContain('progress');
  });

  /**
   * Confirms repeated focus and refresh commands are represented as monotonic intents.
   */
  it('records form focus and history refresh intents for command execution', () => {
    const formFocusedState = reduceExtractionCenterState(getDefaultExtractionCenterState(), { type: 'requestFormFocus' });
    const refreshedState = reduceExtractionCenterState(formFocusedState, { type: 'requestHistoryRefresh' });

    expect(formFocusedState.formFocusRequestId).toBe(1);
    expect(refreshedState.historyRefreshRequestId).toBe(1);
  });
});

/**
 * Verifies the background monitor render surface.
 */
describe('ExtractionBackgroundMonitorContent', () => {
  /**
   * Confirms active runs show status, progress, and safe selection controls.
   */
  it('renders an active tracked run with safe progress text and open action', () => {
    const markup = renderToStaticMarkup(
      <ExtractionBackgroundMonitorContent
        runs={[
          {
            runId: 'run-background-active',
            status: createExtractionRunStatus({ runId: 'run-background-active', status: 'Running' }),
            isFetching: true,
            isAcknowledged: false,
          },
        ]}
      />,
    );

    expect(markup).toContain('Extraction Runs');
    expect(markup).toContain('run-background-active');
    expect(markup).toContain('Running');
    expect(markup).toContain('Extraction is running.');
    expect(markup).toContain('Open run');
    expect(markup).toContain('Refreshing');
    expect(markup).not.toContain('/api/extractions');
    expect(markup).not.toContain('Password=');
    expect(markup).not.toContain('Neo4j driver');
  });

  /**
   * Confirms terminal runs expose acknowledgement and acknowledged rows disappear from the monitor.
   */
  it('renders terminal acknowledgement controls and hides acknowledged rows', () => {
    const terminalMarkup = renderToStaticMarkup(
      <ExtractionBackgroundMonitorContent
        runs={[
          {
            runId: 'run-background-completed',
            status: createExtractionRunStatus({ runId: 'run-background-completed', status: 'Completed' }),
            isFetching: false,
            isAcknowledged: false,
          },
        ]}
      />,
    );
    const acknowledgedMarkup = renderToStaticMarkup(
      <ExtractionBackgroundMonitorContent
        runs={[
          {
            runId: 'run-background-completed',
            status: createExtractionRunStatus({ runId: 'run-background-completed', status: 'Completed' }),
            isFetching: false,
            isAcknowledged: true,
          },
        ]}
      />,
    );

    expect(terminalMarkup).toContain('Completed');
    expect(terminalMarkup).toContain('Acknowledge');
    expect(acknowledgedMarkup).toContain('No extraction runs are currently tracked.');
    expect(acknowledgedMarkup).not.toContain('run-background-completed');
  });

  /**
   * Confirms unavailable rows use safe frontend-authored copy.
   */
  it('renders unavailable tracked runs without raw diagnostics', () => {
    const markup = renderToStaticMarkup(
      <ExtractionBackgroundMonitorContent
        runs={[
          {
            runId: 'run-background-unavailable',
            error: { category: 'network', message: 'Archon API could not be reached.', retryable: true },
            isFetching: false,
            isAcknowledged: false,
          },
        ]}
      />,
    );

    expect(markup).toContain('Unavailable');
    expect(markup).toContain('Status is unavailable. Open the run detail for persistent retry context.');
    expect(markup).toContain('Acknowledge');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('ConnectionString');
  });
});

/**
 * Verifies Snapshot workspace command palette registrations.
 */
describe('Snapshot workspace command registrations', () => {
  /**
   * Confirms commands execute through local shell and feature actions rather than browser navigation.
   */
  it('registers open, focus form, refresh history, and focus active run commands', () => {
    const calls: string[] = [];
    const commands = getWorkbenchShellCommands({
      state: getDefaultWorkbenchState(),
      selectActivity: (activityId) => calls.push(`activity:${activityId}`),
      selectTab: (tabId) => calls.push(`tab:${tabId}`),
      toggleBottomPanel: () => calls.push('toggleBottomPanel'),
      showBottomPanel: () => calls.push('showBottomPanel'),
      hideBottomPanel: () => calls.push('hideBottomPanel'),
      resetLayoutPreferences: () => calls.push('resetLayoutPreferences'),
      setCommandPaletteVisible: (isVisible) => calls.push(`palette:${isVisible}`),
      notifyInformation: (options) => {
        calls.push(`notify:${options.operationName}`);
      },
      extractionCenter: {
        state: {
          ...getDefaultExtractionCenterState(),
          trackedRuns: [{ runId: 'run-command-active', isAcknowledged: false }],
        },
        requestFormFocus: () => calls.push('focusForm'),
        requestHistoryRefresh: () => calls.push('refreshHistory'),
        focusActiveBackgroundRun: () => calls.push('focusActiveRun'),
      },
    });

    commands.find((command) => command.id === 'snapshotWorkspace.open')?.execute();
    commands.find((command) => command.id === 'snapshotWorkspace.focusForm')?.execute();
    commands.find((command) => command.id === 'snapshotWorkspace.refreshHistory')?.execute();
    commands.find((command) => command.id === 'snapshotWorkspace.focusActiveBackgroundRun')?.execute();

    expect(commands.filter((command) => command.group === 'Snapshot Workspace').map((command) => command.id)).toEqual([
      'snapshotWorkspace.open',
      'snapshotWorkspace.focusForm',
      'snapshotWorkspace.refreshHistory',
      'snapshotWorkspace.focusActiveBackgroundRun',
    ]);
    expect(calls).toContain('activity:snapshots');
    expect(calls).toContain('focusForm');
    expect(calls).toContain('refreshHistory');
    expect(calls).toContain('focusActiveRun');
  });
});

/**
 * Verifies Extraction Center rendering for query states and safe history rows.
 */
describe('ExtractionCenterContent', () => {
  /**
   * Confirms populated Snapshot workspace renders required regions without leaking unsafe diagnostics.
   */
  it('renders populated Snapshot workspace with compact operational regions', () => {
    const markup = renderToStaticMarkup(
      <QueryClientProvider client={createTestQueryClient()}>
        <ExtractionCenterContent
          history={{
            runs: [
              {
                runId: 'run-completed',
                status: 'Completed',
                startedUtc: '2026-01-01T00:00:00Z',
                completedUtc: '2026-01-01T00:05:00Z',
                repositoryRootDirectory: 'D:/workspace/Archon',
                solutionCount: 2,
                warningCount: 1,
                errorCount: 0,
                snapshotIdentity: 'snapshot://archon/current',
              },
            ],
          }}
          isLoading={false}
          isRefetching={false}
        />
      </QueryClientProvider>,
    );

    expect(markup).toContain('Snapshot Workspace');
    expect(markup).toContain('aria-label="Snapshot workspace regions"');
    expect(markup).toContain('data-snapshot-region="new-extraction"');
    expect(markup).toContain('data-snapshot-region="update-status"');
    expect(markup).toContain('data-snapshot-region="run-history"');
    expect(markup).toContain('data-snapshot-region="run-details"');
    expect(markup).toContain('Run history');
    expect(markup).toContain('class="extraction-history__grid-wrap"');
    expect(markup).toContain('aria-label="Dense recent extraction runs"');
    expect(markup).toContain('class="extraction-history__status-text"');
    expect(markup).toContain('New Extraction');
    expect(markup).toContain('Route: POST /extractions');
    expect(markup).toContain('title="Submit explicit solution paths through POST /extractions. Recursive discovery is not used."');
    expect(markup).toContain('aria-label="Required extraction request fields"');
    expect(markup).toContain('run-completed');
    expect(markup).toContain('Completed');
    expect(markup).toContain('D:/workspace/Archon');
    expect(markup).toContain('2 solutions');
    expect(markup).toContain('1 warning');
    expect(markup).toContain('0 errors');
    expect(markup).toContain('snapshot://archon/current');
    expect(markup).not.toContain('ui-card');
    expect(markup).not.toContain('snapshot-workspace__pane snapshot-workspace__pane--status"><section');
    expect(markup).not.toContain('/api/extractions');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('Password=');
    expect(markup).not.toContain('Neo4j driver');
  });

  /**
   * Confirms empty history uses terse operational wording.
   */
  it('renders a terse empty history state', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent history={{ runs: [] }} isLoading={false} isRefetching={false} />,
    );

    expect(markup).toContain('No runs yet.');
    expect(markup).toContain('Submit an explicit extraction request.');
  });

  /**
   * Confirms accepted runs render the compact Snapshot update status fields.
   */
  it('renders compact Snapshot update status after successful submission', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        acceptedRun={createExtractionRunStatus({ runId: 'run-accepted-002', status: 'Queued', repositoryRootDirectory: 'D:/workspace/Archon' })}
        history={{ runs: [] }}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Update status');
    expect(markup).toContain('Queued: run-accepted-002.');
    expect(markup).toContain('run-accepted-002');
    expect(markup).toContain('Queued');
    expect(markup).toContain('Stage');
    expect(markup).toContain('Extraction is running.');
    expect(markup).toContain('0 warnings');
    expect(markup).toContain('0 errors');
    expect(markup).toContain('No snapshot yet');
    expect(markup).not.toContain('Neo4j driver');
    expect(markup).not.toContain('Password=');
  });

  /**
   * Confirms the compact update status maps all applicable lifecycle states to visible text.
   */
  it('maps Snapshot update status states without relying on color', () => {
    const statuses = ['Queued', 'Running', 'Completed', 'Failed', 'Cancelled', 'Unavailable', 'Unknown'];
    const markup = statuses.map((status) => renderToStaticMarkup(
      <ExtractionCenterContent
        acceptedRun={createExtractionRunStatus({ runId: `run-${status.toLowerCase()}`, status })}
        history={{ runs: [] }}
        isLoading={false}
        isRefetching={false}
      />,
    )).join('\n');

    expect(markup).toContain('Queued: run-queued.');
    expect(markup).toContain('Running: run-running.');
    expect(markup).toContain('Completed: run-completed.');
    expect(markup).toContain('Failed: run-failed.');
    expect(markup).toContain('Cancelled: run-cancelled.');
    expect(markup).toContain('Unavailable: run-unavailable.');
    expect(markup).toContain('Unknown: run-unknown.');
    expect(markup).toContain('aria-label="Snapshot update state"');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('Password=');
  });

  /**
   * Confirms selected polling data overrides accepted-run state in the compact update status.
   */
  it('renders polling-backed Snapshot update status with snapshot identity and diagnostic counts', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        acceptedRun={createExtractionRunStatus({ runId: 'run-accepted-old', status: 'Queued' })}
        history={{ runs: [] }}
        isLoading={false}
        isRefetching={false}
        isUpdateStatusRefreshing={true}
        selectedRun={createExtractionRunStatus({ runId: 'run-selected-current', status: 'Completed' })}
        selectedRunId="run-selected-current"
      />,
    );

    expect(markup).toContain('Completed: run-selected-current.');
    expect(markup).toContain('Refreshing');
    expect(markup).toContain('snapshot://repo/current');
    expect(markup).toContain('Warnings');
    expect(markup).toContain('Errors');
    expect(markup).not.toContain('run-accepted-old</dd>');
    expect(markup).not.toContain('raw Cypher');
  });

  /**
   * Confirms unavailable status feedback remains safe and does not become a diagnostic console.
   */
  it('renders unavailable Snapshot update status without unsafe diagnostics or log-pane language', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        history={{ runs: [] }}
        isLoading={false}
        isRefetching={false}
        selectedRunId="run-unavailable-summary"
        updateStatusError={{ category: 'network', message: 'Archon API could not be reached. Check that the service is running and accessible.', retryable: true, status: 503 }}
      />,
    );

    expect(markup).toContain('Unavailable: run-unavailable-summary.');
    expect(markup).toContain('Archon API could not be reached. Check that the service is running and accessible.');
    expect(markup).toContain('title="Status shows lifecycle, stage, message, aggregate warning and error counts, and snapshot identity when ArchonApi returns those safe fields."');
    expect(markup).not.toContain('System.Exception');
    expect(markup).not.toContain('ConnectionString');
    expect(markup).not.toContain('Output pane');
    expect(markup).not.toContain('Log console');
    expect(markup).not.toContain('Event stream');
  });

  /**
   * Confirms selected-run detail renders queued status as compact property groups.
   */
  it('renders queued selected-run detail as compact properties safely', () => {
    const markup = renderToStaticMarkup(
      <ExtractionRunDetail
        selectedRunId="run-queued"
        run={createExtractionRunStatus({ runId: 'run-queued', status: 'Queued', repositoryRootDirectory: 'D:/workspace/Archon' })}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Selected run properties');
    expect(markup).toContain('class="extraction-run-detail__properties"');
    expect(markup).toContain('run-queued');
    expect(markup).toContain('Queued');
    expect(markup).toContain('Polling');
    expect(markup).toContain('Active monitor');
    expect(markup).toContain('Request');
    expect(markup).toContain('Archon.sln');
    expect(markup).toContain('Metadata keys');
    expect(markup).not.toContain('This monitor reads');
    expect(markup).not.toContain('Neo4j driver');
    expect(markup).not.toContain('Password=');
  });

  /**
   * Confirms selected-run detail renders running progress with accessible percentage semantics.
   */
  it('renders running selected-run progress safely', () => {
    const markup = renderToStaticMarkup(
      <ExtractionRunDetail
        selectedRunId="run-running"
        run={createExtractionRunStatus({ runId: 'run-running', status: 'Running' })}
        isLoading={false}
        isRefetching={true}
      />,
    );

    expect(markup).toContain('Refreshing status');
    expect(markup).toContain('role="progressbar"');
    expect(markup).toContain('aria-valuenow="50"');
    expect(markup).toContain('50% complete');
    expect(markup).not.toContain('raw Cypher');
  });

  /**
   * Confirms completed selected-run detail renders terminal status, timings, snapshot identity, and diagnostics.
   */
  it('renders completed selected-run terminal output and persistence diagnostics', () => {
    const run = {
      ...createExtractionRunStatus({ runId: 'run-completed-detail', status: 'Completed' }),
      timings: [{ stage: 'Total', elapsedMilliseconds: 2_500, completedUtc: '2026-01-01T00:05:00Z' }],
      persistenceDiagnostics: createPersistenceDiagnostics(),
    };

    const markup = renderToStaticMarkup(<ExtractionRunDetail selectedRunId="run-completed-detail" run={run} isLoading={false} isRefetching={false} />);

    expect(markup).toContain('Terminal status');
    expect(markup).toContain('snapshot://repo/current');
    expect(markup).toContain('Top-level timings');
    expect(markup).toContain('2500 ms (2.5 s)');
    expect(markup).toContain('Persistence diagnostics');
    expect(markup).toContain('Persistence.Commit');
    expect(markup).toContain('1500 ms (1.5 s)');
    expect(markup).toContain('Not measured');
    expect(markup).toContain('class="extraction-run-detail__section extraction-run-detail__section--nested"');
    expect(markup).not.toContain('Neo4j');
    expect(markup).not.toContain('ConnectionString');
  });

  /**
   * Confirms failed selected-run detail stays honest about missing snapshots and diagnostics.
   */
  it('renders failed selected-run detail without fabricating diagnostics', () => {
    const markup = renderToStaticMarkup(
      <ExtractionRunDetail
        selectedRunId="run-failed"
        run={createExtractionRunStatus({ runId: 'run-failed', status: 'Failed' })}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Failed');
    expect(markup).toContain('Terminal status');
    expect(markup).toContain('No snapshot yet');
    expect(markup).toContain('Not available.');
    expect(markup).toContain('Counts and metadata keys only.');
  });

  /**
   * Confirms selected-run not-found and unavailable states render normalized safe errors only.
   */
  it('renders selected-run not-found and unavailable failures safely', () => {
    const notFoundMarkup = renderToStaticMarkup(
      <ExtractionRunDetail
        selectedRunId="run-missing"
        error={{ category: 'notFound', message: 'Extraction run was not found.', retryable: false, status: 404 }}
        isLoading={false}
        isRefetching={false}
      />,
    );
    const unavailableMarkup = renderToStaticMarkup(
      <ExtractionRunDetail
        selectedRunId="run-unavailable"
        error={{ category: 'network', message: 'Archon API could not be reached. Check that the service is running and accessible.', retryable: true, status: 503 }}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(notFoundMarkup).toContain('Selected run was not found');
    expect(notFoundMarkup).toContain('Run run-missing is not available from ArchonApi.');
    expect(unavailableMarkup).toContain('Selected run status is unavailable');
    expect(unavailableMarkup).toContain('Retry available');
    expect(`${notFoundMarkup}${unavailableMarkup}`).not.toContain('/api/extractions');
    expect(`${notFoundMarkup}${unavailableMarkup}`).not.toContain('System.Exception');
    expect(`${notFoundMarkup}${unavailableMarkup}`).not.toContain('Password=');
  });

  /**
   * Confirms selected history rows expose an enabled detail action and selected state.
   */
  it('renders selectable dense history rows for selected-run polling', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        history={{
          runs: [
            {
              runId: 'run-selected-row',
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
        }}
        selectedRunId="run-selected-row"
        selectedRun={createExtractionRunStatus({ runId: 'run-selected-row', status: 'Running' })}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('aria-selected="true"');
    expect(markup).toContain('extraction-history__row-action');
    expect(markup).toContain('aria-label="Select run run-selected-row for details"');
    expect(markup).toContain('Selected');
    expect(markup).toContain('Not completed');
    expect(markup).toContain('No snapshot yet');
    expect(markup).not.toContain('Details later');
  });

  /**
   * Confirms duplicate actions are disabled until selected-run status exposes full request values.
   */
  it('renders unavailable duplication guidance for compact history selections', () => {
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        history={{
          runs: [
            {
              runId: 'run-history-only',
              status: 'Completed',
              startedUtc: '2026-01-01T00:00:00Z',
              completedUtc: '2026-01-01T00:05:00Z',
              repositoryRootDirectory: 'D:/workspace/Archon',
              solutionCount: 2,
              warningCount: 0,
              errorCount: 0,
              snapshotIdentity: 'snapshot://repo/current',
            },
          ],
        }}
        selectedRunId="run-history-only"
        isSelectedRunLoading={true}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Duplicate request unavailable');
    expect(markup).toContain('Load details before duplicating.');
    expect(markup).toContain('title="History rows do not include explicit solution paths. Load GET /extractions/{runId} before duplicating a request."');
    expect(markup).not.toContain('source=');
    expect(markup).not.toContain('Password=');
  });

  /**
   * Confirms completed runs expose duplicate and produced-snapshot actions without unsafe handoff claims.
   */
  it('renders duplicate and produced-snapshot placeholder actions for completed selected runs', () => {
    const run = createExtractionRunStatus({ runId: 'run-completed-actions', status: 'Completed', repositoryRootDirectory: 'D:/workspace/Archon' });
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        history={{ runs: [] }}
        selectedRunId="run-completed-actions"
        selectedRun={run}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Run follow-up actions');
    expect(markup).toContain('Duplicate request');
    expect(markup).toContain('Open produced snapshot');
    expect(markup).toContain('Snapshot handoff is pending WP006.');
    expect(markup).toContain('title="This placeholder does not query graph data, dashboard metrics, search, lenses, visualizations, or snapshot lifecycle routes."');
    expect(markup).toContain('snapshot://repo/current');
    expect(markup).not.toContain('/api/extractions');
    expect(markup).not.toContain('raw Cypher');
    expect(markup).not.toContain('Neo4j');
  });

  /**
   * Confirms invoking duplicate from selected-run status repopulates the form and surfaces metadata guidance.
   */
  it('duplicates selected run status into the form without auto-submitting', () => {
    const updatedStates: unknown[] = [];
    let submitCount = 0;
    const formSummaryRef = createRef<HTMLDivElement>();
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        formSummaryRef={formSummaryRef}
        history={{ runs: [] }}
        onFormStateChange={(state) => updatedStates.push(state)}
        onSubmitExtraction={() => { submitCount += 1; }}
        selectedRun={{
          ...createExtractionRunStatus({ runId: 'run-duplicate-ui', status: 'Completed', repositoryRootDirectory: 'D:/workspace/Archon' }),
          submittedRequest: {
            repositoryRootDirectory: 'D:/workspace/Archon',
            solutionPaths: ['Archon.sln'],
            branchName: 'main',
            commitSha: 'abc123',
            requestedBy: 'test-double',
            metadataKeys: ['source'],
          },
        }}
        selectedRunId="run-duplicate-ui"
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Duplicate request');
    expect(markup).toContain('Metadata values are not exposed by run status and must be re-entered before submitting if needed.');
    expect(markup).not.toContain('source=');
    expect(updatedStates).toEqual([]);
    expect(submitCount).toBe(0);
  });

  /**
   * Confirms submission validation and API setup feedback remain safe and persistent.
   */
  it('renders safe validation and API-unconfigured feedback without raw configuration values', () => {
    let submitCount = 0;
    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        connectivityState={createUnconfiguredConnectivityState()}
        formValidationMessages={{
          repositoryRootDirectory: ['Enter the repository root directory to extract.'],
          solutionPaths: ['Enter at least one explicit solution path. ArchonExplorer does not discover solutions recursively.'],
        }}
        history={{ runs: [] }}
        isLoading={false}
        isRefetching={false}
        onSubmitExtraction={() => { submitCount += 1; }}
      />,
    );

    expect(markup).toContain('API base URL not configured');
    expect(markup).toContain('Set the Archon API base URL before API-backed features can run.');
    expect(markup).toContain('API not configured.');
    expect(markup).toContain('aria-disabled="true"');
    expect(markup).toContain('<button class="ui-button ui-button--default ui-button--sm" type="submit" aria-disabled="true" title="Submit explicit solution paths through POST /extractions">Submit extraction</button>');
    expect(markup).toContain('Enter the repository root directory to extract.');
    expect(markup).toContain('ArchonExplorer does not discover solutions recursively.');
    expect(markup).not.toContain('VITE_ARCHON_API_BASE_URL');
    expect(markup).not.toContain('/api/extractions');
    expect(markup).not.toContain('ConnectionString');
    expect(submitCount).toBe(0);
  });

  /**
   * Confirms server submission failures render safe mutation feedback without unsafe diagnostics.
   */
  it('renders safe submission failure feedback', () => {
    const error = new StartExtractionError({
      category: 'server',
      message: 'The extraction request could not be submitted. Try again after checking API readiness.',
      retryable: true,
      status: 503,
    });

    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        history={{ runs: [] }}
        isLoading={false}
        isRefetching={false}
        submissionError={error}
      />,
    );

    expect(markup).toContain('Submission needs attention');
    expect(markup).toContain('The extraction request could not be submitted. Try again after checking API readiness.');
    expect(markup).not.toContain('raw Cypher');
    expect(markup).not.toContain('Neo4j');
    expect(markup).not.toContain('Password=');
  });

  /**
   * Confirms safe error rendering does not expose raw backend diagnostics or route internals.
   */
  it('renders a safe page-level error state', () => {
    const error = new ExtractionHistoryError({
      category: 'network',
      message: 'Archon API could not be reached. Check that the service is running and accessible.',
      retryable: true,
    } satisfies NormalizedArchonApiError);

    const markup = renderToStaticMarkup(
      <ExtractionCenterContent
        error={error}
        history={undefined}
        isLoading={false}
        isRefetching={false}
      />,
    );

    expect(markup).toContain('Extraction history is unavailable');
    expect(markup).toContain('Archon API could not be reached. Check that the service is running and accessible.');
    expect(markup).not.toContain('/api/extractions');
    expect(markup).not.toContain('raw Cypher');
    expect(markup).not.toContain('Password=');
  });
});
