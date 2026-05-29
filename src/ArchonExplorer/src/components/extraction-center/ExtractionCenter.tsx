import { createRef, useEffect, useRef, useState, type RefObject } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { ArchonApiClient } from '@/api/archonApiClient';
import { createConfiguredConnectivityState, type ApiConnectivityState } from '@/api/connectivity';
import type { ExtractionRunHistoryResponse, ExtractionRunStatusResponse, ExtractionRunSummaryResponse } from '@/api/archonApiTypes';
import { isExtractionRunTerminalStatus } from '@/api/polling';
import { ExtractionRequestForm } from '@/components/extraction-center/ExtractionRequestForm';
import { ExtractionRunDetail, type ExtractionRunDetailError } from '@/components/extraction-center/ExtractionRunDetail';
import { ExtractionRunSummary } from '@/components/extraction-center/ExtractionRunSummary';
import { createInitialExtractionRequestFormState, mapExtractionRequestFormStateToRequest, mapRunStatusToDuplicateFormState, mapServerValidationIssuesToFormMessages, type ExtractionRequestFormState, type ExtractionRequestFormValidationMessages } from '@/components/extraction-center/extractionFormState';
import { ExtractionHistoryError, useExtractionHistory, type ExtractionHistoryClient } from '@/hooks/useExtractionHistory';
import { useExtractionRunPolling } from '@/hooks/useExtractionRunPolling';
import { StartExtractionError, useStartExtraction, type StartExtractionClient } from '@/hooks/useStartExtraction';
import { useApiConnectivity } from '@/hooks/useApiConnectivity';
import type { NotificationRuntime } from '@/providers/NotificationProvider';
import { useNotifications } from '@/providers/NotificationProvider';
import { useExtractionCenterStore } from '@/state/extractionCenterStore';

/**
 * Defines the default number of recent extraction runs requested by the feature surface.
 */
const defaultHistoryTake = 20;

/**
 * Describes the notification helper subset used when selected extraction status changes.
 */
type ExtractionRunStatusNotificationRuntime = Pick<NotificationRuntime, 'notifyError' | 'notifyInformation' | 'notifySuccess' | 'notifyWarning'>;

/**
 * Describes the inputs needed to publish one safe extraction run transition notification.
 */
interface PublishExtractionRunStatusNotificationOptions extends ExtractionRunStatusNotificationRuntime {
  /**
   * Contains the public run identifier whose status changed.
   */
  readonly runId: string;

  /**
   * Contains the latest safe status response when polling succeeded.
   */
  readonly status?: ExtractionRunStatusResponse;

  /**
   * Contains the latest safe polling error when status became unavailable.
   */
  readonly error?: ExtractionRunDetailError;
}

/**
 * Describes the typed client surface consumed by the integrated Snapshot workspace feature.
 */
export type ExtractionCenterClient = ExtractionHistoryClient & StartExtractionClient & Pick<ArchonApiClient, 'getExtractionStatus' | 'getHealth' | 'getReadiness'>;

/**
 * Describes inputs accepted by the Snapshot workspace feature entry component.
 */
export interface ExtractionCenterProps {
  /**
   * Supplies a production client or deterministic test double for loading recent runs and starting runs.
   */
  readonly client?: ExtractionCenterClient;
}

/**
 * Describes the render-only state accepted by the Snapshot workspace content component.
 */
export interface ExtractionCenterContentProps {
  /**
   * Contains the current history payload when a query has loaded successfully.
   */
  readonly history?: ExtractionRunHistoryResponse;

  /**
   * Contains the safe page-level query error when history cannot be loaded.
   */
  readonly error?: ExtractionHistoryError;

  /**
   * Indicates that the initial history request is still in progress.
   */
  readonly isLoading: boolean;

  /**
   * Indicates that a previously loaded history list is being refreshed.
   */
  readonly isRefetching: boolean;

  /**
   * Requests a safe retry of the current history query.
   */
  readonly onRetry?: () => void;

  /**
   * Contains the browser-owned start-extraction form values.
   */
  readonly formState?: ExtractionRequestFormState;

  /**
   * Contains safe validation messages for the start-extraction form.
   */
  readonly formValidationMessages?: ExtractionRequestFormValidationMessages;

  /**
   * Contains the latest safe submission error when POST /extractions failed.
   */
  readonly submissionError?: StartExtractionError;

  /**
   * Contains safe API readiness state used by the form.
   */
  readonly connectivityState?: ApiConnectivityState;

  /**
   * Indicates whether a start-extraction mutation is in flight.
   */
  readonly isSubmitting?: boolean;

  /**
   * Contains the accepted run currently selected by the form workflow.
   */
  readonly acceptedRun?: ExtractionRunStatusResponse;

  /**
   * Contains the run identifier currently selected for detailed monitoring.
   */
  readonly selectedRunId?: string;

  /**
   * Contains the latest polling-backed run detail when one has loaded successfully.
   */
  readonly selectedRun?: ExtractionRunStatusResponse;

  /**
   * Contains the safe polling failure when the selected run cannot be loaded.
   */
  readonly selectedRunError?: ExtractionRunDetailError;

  /**
   * Indicates that the selected run detail is performing its first status request.
   */
  readonly isSelectedRunLoading?: boolean;

  /**
   * Indicates that a loaded selected run is refreshing in the background.
   */
  readonly isSelectedRunRefetching?: boolean;

  /**
   * Contains the latest polling-backed status failure for the compact update surface.
   */
  readonly updateStatusError?: ExtractionRunDetailError;

  /**
   * Indicates whether the compact update status surface is currently refreshing.
   */
  readonly isUpdateStatusRefreshing?: boolean;

  /**
   * Receives edited browser-owned form state.
   */
  readonly onFormStateChange?: (state: ExtractionRequestFormState) => void;

  /**
   * Requests validation and submission of the current form values.
   */
  readonly onSubmitExtraction?: () => void;

  /**
   * Selects a run from recent history for polling-backed detail monitoring.
   */
  readonly onSelectRun?: (runId: string) => void;

  /**
   * Publishes the produced-snapshot placeholder notification for the supplied snapshot identity.
   */
  readonly onOpenProducedSnapshot?: (snapshotIdentity: string) => void;

  /**
   * Provides a focus target for duplicate-request workflows that repopulate the form.
   */
  readonly formSummaryRef?: RefObject<HTMLDivElement | null>;
}

/**
 * Renders the API-backed Snapshot workspace surface.
 *
 * @param props Contains optional runtime dependencies for deterministic tests.
 * @param props.client Optional typed API client override used instead of the production client.
 * @returns The Snapshot workspace feature surface with extraction workflow states.
 */
export function ExtractionCenter({ client }: ExtractionCenterProps) {
  // Form values are browser-owned interaction state. They are deliberately separate from
  // server-owned history and run status data, which stay in TanStack Query and mutation results.
  const [formState, setFormState] = useState<ExtractionRequestFormState>(() => createInitialExtractionRequestFormState());
  const [formValidationMessages, setFormValidationMessages] = useState<ExtractionRequestFormValidationMessages>({});
  const [acceptedRun, setAcceptedRun] = useState<ExtractionRunStatusResponse | undefined>();
  const [selectedRunId, setSelectedRunId] = useState<string | undefined>();
  const formSummaryRef = useRef<HTMLDivElement | null>(null);
  const connectivityState = useApiConnectivity({ client });
  const notifications = useNotifications();
  const extractionCenterStore = useExtractionCenterStore();
  // The feature asks the hook for query options, then lets TanStack Query own all server state,
  // loading, refetching, and cancellation behavior instead of duplicating history in component state.
  const historyQuery = useQuery(useExtractionHistory({ client, take: defaultHistoryTake }));
  const startExtractionMutation = useStartExtraction({
    client,
    onAccepted: (run) => {
      // Selecting the accepted run starts the same polling-backed detail workflow used by history
      // rows while preserving the immediate accepted-run summary from the mutation response.
      setAcceptedRun(run);
      extractionCenterStore.selectRun(run.runId);
      setFormValidationMessages({});
      notifications.notifyInformation({ operationName: 'Extraction accepted', detail: `Run ${run.runId} was accepted by ArchonApi.` });
    },
  });
  const selectedRunPolling = useExtractionRunPolling({
    client,
    enabled: selectedRunId !== undefined,
    runId: selectedRunId,
  });

  useEffect(() => {
    // Shared selected-run state lets the bottom panel and command palette return to the same
    // detail monitor without moving server-owned status payloads into browser-local state.
    setSelectedRunId(extractionCenterStore.state.selectedRunId);
  }, [extractionCenterStore.state.selectedRunId]);

  useEffect(() => {
    // A form-focus command is represented as an incrementing intent so repeated commands remain
    // observable even when the target and selected run do not otherwise change.
    if (extractionCenterStore.state.formFocusRequestId > 0) {
      formSummaryRef.current?.focus();
    }
  }, [extractionCenterStore.state.formFocusRequestId]);

  useEffect(() => {
    // History refresh remains a command intent rather than a local history copy; TanStack Query
    // owns the actual server-state refresh and cache update.
    if (extractionCenterStore.state.historyRefreshRequestId > 0) {
      void historyQuery.refetch();
    }
  }, [extractionCenterStore.state.historyRefreshRequestId, historyQuery]);

  useEffect(() => {
    // Accepted runs and selected history rows should appear in the background monitor so users can
    // navigate away while long-running extraction continues polling from the bottom panel.
    if (acceptedRun !== undefined) {
      extractionCenterStore.trackRun(acceptedRun.runId);
    }
  }, [acceptedRun, extractionCenterStore]);

  useEffect(() => {
    // Selected runs are also tracked even when they originated from compact history rather than a
    // fresh submission, because they may still be active or need terminal acknowledgement.
    if (selectedRunId !== undefined) {
      extractionCenterStore.trackRun(selectedRunId);
    }
  }, [extractionCenterStore, selectedRunId]);

  useEffect(() => {
    // Transition notifications are supplemental and deduplicated by remembered status. Persistent
    // selected-run errors and terminal detail remain visible in the page or bottom-panel row.
    if (selectedRunId === undefined) {
      return;
    }

    const trackedRun = extractionCenterStore.state.trackedRuns.find((run) => run.runId === selectedRunId);
    const statusForNotification = selectedRunPolling.error === undefined
      ? selectedRunPolling.status?.status
      : 'Unavailable';

    if (statusForNotification === undefined || trackedRun?.lastNotifiedStatus === statusForNotification) {
      return;
    }

    publishExtractionRunStatusNotification({
      notifyError: notifications.notifyError,
      notifyInformation: notifications.notifyInformation,
      notifySuccess: notifications.notifySuccess,
      notifyWarning: notifications.notifyWarning,
      runId: selectedRunId,
      error: selectedRunPolling.error,
      status: selectedRunPolling.status,
    });
    extractionCenterStore.recordNotifiedStatus(selectedRunId, statusForNotification);
  }, [extractionCenterStore, notifications.notifyError, notifications.notifyInformation, notifications.notifySuccess, notifications.notifyWarning, selectedRunId, selectedRunPolling.error, selectedRunPolling.status]);

  /**
   * Validates the current form state and starts the extraction mutation when valid.
   */
  function handleSubmitExtraction(): void {
    // Browser validation catches obvious missing values only. Server validation remains
    // authoritative and is mapped back into the same persistent form feedback surface.
    if (connectivityState.status === 'unconfigured') {
      setFormValidationMessages({
        form: [connectivityState.description ?? connectivityState.label],
      });
      formSummaryRef.current?.focus();
      return;
    }

    const mappingResult = mapExtractionRequestFormStateToRequest(formState);
    if (!mappingResult.ok) {
      setFormValidationMessages(mappingResult.validationMessages);
      return;
    }

    setFormValidationMessages({});
    startExtractionMutation.mutate(mappingResult.request, {
      onError: (error) => {
        // Safe validation issues become field messages when the API supplies them; all failures
        // also remain visible as persistent form-level feedback through the mutation error.
        setFormValidationMessages(mapServerValidationIssuesToFormMessages(error.validationIssues));
      },
    });
  }

  return (
    <ExtractionCenterContent
      acceptedRun={acceptedRun}
      connectivityState={connectivityState}
      error={historyQuery.error ?? undefined}
      formState={formState}
      formSummaryRef={formSummaryRef}
      formValidationMessages={formValidationMessages}
      history={historyQuery.data}
      isLoading={historyQuery.isLoading}
      isRefetching={historyQuery.isRefetching && !historyQuery.isLoading}
      isSelectedRunLoading={selectedRunId !== undefined && selectedRunPolling.status === undefined && selectedRunPolling.isFetching}
      isSelectedRunRefetching={selectedRunPolling.status !== undefined && selectedRunPolling.isFetching}
      isUpdateStatusRefreshing={selectedRunPolling.isFetching || startExtractionMutation.isPending}
      isSubmitting={startExtractionMutation.isPending}
      onFormStateChange={setFormState}
      onRetry={() => void historyQuery.refetch()}
      onSelectRun={extractionCenterStore.selectRun}
      onOpenProducedSnapshot={(snapshotIdentity) => notifications.notifyInformation({
        operationName: 'Produced snapshot handoff',
        detail: `Snapshot context is not active yet for ${snapshotIdentity}. WP006 owns opening produced snapshots for dashboards, search, graph views, and lenses.`,
      })}
      onSubmitExtraction={handleSubmitExtraction}
      selectedRun={selectedRunPolling.status}
      selectedRunError={selectedRunPolling.error}
      selectedRunId={selectedRunId}
      submissionError={startExtractionMutation.error ?? undefined}
      updateStatusError={selectedRunPolling.error}
    />
  );
}

/**
 * Renders the Snapshot workspace content for a supplied query state.
 *
 * @param props Contains form, accepted-run, history, safe error, and query-state flags to render.
 * @param props.history The loaded extraction history response, when available.
 * @param props.error The safe page-level error, when the history query failed.
 * @param props.isLoading Indicates whether the initial request is still loading.
 * @param props.isRefetching Indicates whether a background refresh is currently active.
 * @param props.onRetry Optional callback used by users to retry a safe failed request.
 * @param props.formState The current browser-owned form values.
 * @param props.formValidationMessages Safe validation messages for the form.
 * @param props.submissionError Latest safe submission failure.
 * @param props.connectivityState Safe API readiness state for guarded submission.
 * @param props.isSubmitting Indicates whether POST /extractions is currently in flight.
 * @param props.acceptedRun The latest accepted run summary returned by the start workflow.
 * @param props.selectedRunId The run identifier selected for polling-backed detail.
 * @param props.selectedRun Latest selected-run status returned by polling.
 * @param props.selectedRunError Safe selected-run polling failure.
 * @param props.isSelectedRunLoading Indicates whether initial selected-run status is loading.
 * @param props.isSelectedRunRefetching Indicates whether selected-run status is refreshing.
 * @param props.updateStatusError Safe polling failure for the compact update status region.
 * @param props.isUpdateStatusRefreshing Indicates whether the compact update status region is actively refreshing.
 * @param props.onFormStateChange Receives edited form state.
 * @param props.onSubmitExtraction Requests validation and submission.
 * @param props.onSelectRun Selects a run from history for detail monitoring.
 * @returns A desktop-style Snapshot workspace showing request, status, history, and details regions.
 */
export function ExtractionCenterContent({ history, error, isLoading, isRefetching, onRetry, formState = createInitialExtractionRequestFormState(), formValidationMessages = {}, submissionError, connectivityState = createConfiguredConnectivityState(), isSubmitting = false, acceptedRun, selectedRunId, selectedRun, selectedRunError, isSelectedRunLoading = false, isSelectedRunRefetching = false, updateStatusError, isUpdateStatusRefreshing = false, onFormStateChange = () => undefined, onSubmitExtraction = () => undefined, onSelectRun = () => undefined, onOpenProducedSnapshot = () => undefined, formSummaryRef }: ExtractionCenterContentProps) {
  // This component is intentionally render-only so tests can assert every state without mocking
  // TanStack internals, while the parent component remains responsible for query execution.
  const runs = history?.runs.filter((run) => run.runId.trim().length > 0) ?? [];
  const displayedRunId = selectedRunId ?? acceptedRun?.runId;
  const displayedRun = selectedRun ?? (selectedRunId === undefined ? acceptedRun : undefined);
  const updateStatusRun = selectedRun ?? acceptedRun;
  const updateStatusRunId = selectedRunId ?? acceptedRun?.runId;
  const stableFormSummaryRef = formSummaryRef ?? createRef<HTMLDivElement>();

  /**
   * Copies the selected run request summary into the form without submitting it.
   */
  function handleDuplicateSelectedRun(): void {
    // Duplication intentionally uses the status response instead of compact history so solution
    // paths are copied only when the API has already exposed them safely.
    if (displayedRun === undefined) {
      return;
    }

    const duplicateResult = mapRunStatusToDuplicateFormState(displayedRun);
    if (!duplicateResult.ok) {
      return;
    }

    onFormStateChange(duplicateResult.formState);

    // Focus moves back to the persistent form summary so keyboard and screen-reader users receive
    // context that the editable request was repopulated and still needs review before submission.
    stableFormSummaryRef.current?.focus();
  }

  /**
   * Publishes the honest produced-snapshot placeholder used before WP006 snapshot context exists.
   */
  function handleOpenProducedSnapshot(): void {
    // The current slice intentionally stops at notification and inline explanation. It does not call
    // graph, dashboard, search, lens, visualization, or snapshot lifecycle routes.
    if (displayedRun?.snapshotIdentity === null || displayedRun?.snapshotIdentity === undefined) {
      return;
    }

    onOpenProducedSnapshot(displayedRun.snapshotIdentity);
  }

  const duplicateResult = displayedRun === undefined ? undefined : mapRunStatusToDuplicateFormState(displayedRun);

  return (
    <section aria-labelledby="snapshot-workspace-title" className="extraction-center snapshot-workspace">
      <header className="extraction-center__header">
        <div className="extraction-center__title-group">
          <h1 id="snapshot-workspace-title">Snapshot Workspace</h1>
          <p title="Primary workbench context for New Extraction, update status, recent runs, and selected run properties.">Extraction operations.</p>
        </div>
        <div className="extraction-center__header-actions" aria-label="Snapshot workspace status">
          {isRefetching ? <Badge variant="outline">Refreshing</Badge> : <Badge variant="outline">Current</Badge>}
        </div>
      </header>
      <div className="snapshot-workspace__regions" aria-label="Snapshot workspace regions">
        <div className="snapshot-workspace__pane snapshot-workspace__pane--new-extraction" data-snapshot-region="new-extraction">
          <ExtractionRequestForm
            connectivityState={connectivityState}
            duplicateNotice={duplicateResult?.ok === true && duplicateResult.validationMessages.form !== undefined ? duplicateResult.validationMessages.form[0] : undefined}
            formSummaryRef={stableFormSummaryRef}
            isSubmitting={isSubmitting}
            onStateChange={onFormStateChange}
            onSubmit={onSubmitExtraction}
            state={formState}
            submissionError={submissionError}
            validationMessages={formValidationMessages}
          />
        </div>
        <div className="snapshot-workspace__pane snapshot-workspace__pane--status" data-snapshot-region="update-status">
          <ExtractionRunSummary error={updateStatusError} isRefreshing={isUpdateStatusRefreshing} run={updateStatusRun} runId={updateStatusRunId} />
        </div>
        <section aria-labelledby="extraction-history-title" className="extraction-history snapshot-workspace__pane snapshot-workspace__pane--history" data-snapshot-region="run-history">
          <div className="extraction-history__heading">
            <div>
              <h2 id="extraction-history-title">Run history</h2>
              <p title="History is loaded from GET /extractions through the typed ArchonApi client.">Recent runs.</p>
            </div>
            {onRetry !== undefined ? (
            <Button type="button" variant="outline" size="sm" onClick={onRetry} title="Refresh recent runs from GET /extractions">
                Refresh
              </Button>
            ) : null}
          </div>
          {renderHistoryState({ error, isLoading, onSelectRun, runs, selectedRunId: displayedRunId })}
        </section>
        <div className="snapshot-workspace__pane snapshot-workspace__pane--details" data-snapshot-region="run-details">
          <ExtractionRunDetail
            duplicateRequestUnavailableReason={selectedRunId !== undefined && displayedRun === undefined ? 'Load details before duplicating.' : duplicateResult?.ok === false ? duplicateResult.reason : undefined}
            error={selectedRunError}
            isLoading={isSelectedRunLoading}
            isRefetching={isSelectedRunRefetching}
            onDuplicateRequest={displayedRun !== undefined && duplicateResult?.ok === true ? handleDuplicateSelectedRun : undefined}
            onOpenProducedSnapshot={displayedRun?.snapshotIdentity === null || displayedRun?.snapshotIdentity === undefined ? undefined : handleOpenProducedSnapshot}
            run={displayedRun}
            selectedRunId={displayedRunId}
          />
        </div>
      </div>
    </section>
  );
}

/**
 * Describes the state needed to render one extraction history body.
 */
interface RenderHistoryStateOptions {
  /**
   * Contains the safe page-level error when the history query failed.
   */
  readonly error?: ExtractionHistoryError;

  /**
   * Indicates whether the initial history request is currently loading.
   */
  readonly isLoading: boolean;

  /**
   * Contains the recent run summaries to render when available.
   */
  readonly runs: readonly ExtractionRunSummaryResponse[];

  /**
   * Contains the selected run identifier so the history row can expose selected state.
   */
  readonly selectedRunId?: string;

  /**
   * Selects a history run for detailed polling.
   */
  readonly onSelectRun: (runId: string) => void;
}

/**
 * Renders the correct history body for loading, error, empty, or populated states.
 *
 * @param options Contains the query-state flags and run collection to evaluate.
 * @returns A React node representing the current history state.
 */
function renderHistoryState({ error, isLoading, onSelectRun, runs, selectedRunId }: RenderHistoryStateOptions) {
  // The ordering gives loading first paint priority, then persistent safe errors, then empty
  // guidance, and finally the compact history table for populated API responses.
  if (isLoading) {
    return <HistoryNotice title="Loading runs." message="Reading runs." />;
  }

  if (error !== undefined) {
    return <HistoryErrorNotice error={error} />;
  }

  if (runs.length === 0) {
    return <HistoryNotice title="No runs yet." message="Submit an explicit extraction request." />;
  }

  return <HistoryGrid onSelectRun={onSelectRun} runs={runs} selectedRunId={selectedRunId} />;
}

/**
 * Publishes a safe transient notification for a selected extraction run status transition.
 *
 * @param options Contains notification helpers, run identity, latest status, and optional safe error.
 */
function publishExtractionRunStatusNotification(options: PublishExtractionRunStatusNotificationOptions): void {
  // Notifications supplement the durable detail and bottom-panel surfaces. The helper intentionally
  // uses frontend-authored copy around public run identifiers and normalized errors only.
  if (options.error !== undefined) {
    options.notifyError(options.error, {
      operationName: 'Extraction status unavailable',
      detail: `Run ${options.runId} status is unavailable. The selected run detail keeps persistent retry context visible.`,
      requiresPersistentDisplay: true,
    });
    return;
  }

  if (options.status === undefined) {
    return;
  }

  const status = options.status;
  if (isExtractionRunTerminalStatus(status.status)) {
    publishTerminalExtractionRunNotification(options, status);
    return;
  }

  options.notifyInformation({
    operationName: 'Extraction run active',
    detail: `Run ${options.runId} is ${options.status.status}. Progress: ${options.status.progress.stage}.`,
  });
}

/**
 * Publishes a terminal-state notification for a selected extraction run.
 *
 * @param options Contains notification helpers and the terminal status response.
 */
function publishTerminalExtractionRunNotification(options: PublishExtractionRunStatusNotificationOptions, status: ExtractionRunStatusResponse): void {
  // Terminal states are split by lifecycle category so completion, failure, cancellation, and
  // unavailable outcomes use wording and notification severity that match the user's next action.
  const normalizedStatus = status.status.trim().toLowerCase();
  const detail = `Run ${options.runId} reached ${status.status}. ${status.progress.message}`;

  if (normalizedStatus === 'completed' || normalizedStatus === 'succeeded' || normalizedStatus === 'success') {
    options.notifySuccess({ operationName: 'Extraction completed', detail });
    return;
  }

  if (normalizedStatus === 'failed' || normalizedStatus === 'failure' || normalizedStatus === 'faulted') {
    options.notifyWarning({ operationName: 'Extraction failed', detail });
    return;
  }

  if (normalizedStatus === 'canceled' || normalizedStatus === 'cancelled') {
    options.notifyWarning({ operationName: 'Extraction cancelled', detail });
    return;
  }

  options.notifyWarning({ operationName: 'Extraction terminal state', detail });
}

/**
 * Describes the common notice inputs used by non-populated history states.
 */
interface HistoryNoticeProps {
  /**
   * Provides the notice heading.
   */
  readonly title: string;

  /**
   * Provides the notice explanatory body text.
   */
  readonly message: string;
}

/**
 * Renders a safe non-error notice for loading and empty history states.
 *
 * @param props Contains the notice title and explanatory text.
 * @param props.title The visible notice title.
 * @param props.message The visible safe explanation for the state.
 * @returns A status region for the current non-error history state.
 */
function HistoryNotice({ title, message }: HistoryNoticeProps) {
  // Native status semantics make loading and empty state updates understandable to assistive tech.
  return (
    <div className="extraction-history__notice" role="status">
      <div>
        <h3>{title}</h3>
        <p>{message}</p>
      </div>
    </div>
  );
}

/**
 * Describes the safe error notice inputs.
 */
interface HistoryErrorNoticeProps {
  /**
   * Contains the normalized safe history error to present.
   */
  readonly error: ExtractionHistoryError;
}

/**
 * Renders a safe persistent error state for failed history requests.
 *
 * @param props Contains the normalized safe error to display.
 * @param props.error The safe query error created by the history hook.
 * @returns An alert region that avoids raw backend diagnostics.
 */
function HistoryErrorNotice({ error }: HistoryErrorNoticeProps) {
  // The error object is already normalized. The UI still avoids route URLs, stack traces,
  // database terminology, or other implementation details in the surrounding text.
  return (
    <div className="extraction-history__notice extraction-history__notice--error" role="alert">
      <div>
        <h3>Extraction history is unavailable</h3>
        <p>{error.message}</p>
        {error.retryable ? <Badge variant="outline">Retry available</Badge> : <Badge variant="warning">Check setup</Badge>}
      </div>
    </div>
  );
}

/**
 * Describes the populated history grid inputs.
 */
interface HistoryGridProps {
  /**
   * Contains recent extraction run summaries from the API.
   */
  readonly runs: readonly ExtractionRunSummaryResponse[];

  /**
   * Contains the selected run identifier so the active row can be announced.
   */
  readonly selectedRunId?: string;

  /**
   * Selects a row for polling-backed detail monitoring.
   */
  readonly onSelectRun: (runId: string) => void;
}

/**
 * Renders recent extraction runs as a compact workbench history grid.
 *
 * @param props Contains the run summaries and selection callback to display.
 * @param props.runs The recent extraction run summaries returned by ArchonApi.
 * @param props.selectedRunId The currently selected run identifier, if any.
 * @param props.onSelectRun Receives a row-selected run identifier.
 * @returns A table with accessible row-selection affordances for polling-backed detail.
 */
function HistoryGrid({ runs, selectedRunId, onSelectRun }: HistoryGridProps) {
  // Selection remains a local interaction event. The selected status response itself is still
  // server state owned by the polling hook and TanStack Query.
  return (
    <div className="extraction-history__grid-wrap" data-scroll-region="run-history-grid">
      <table className="extraction-history__grid" aria-label="Dense recent extraction runs">
        <caption>Recent extraction runs loaded from GET /extractions.</caption>
        <thead>
          <tr>
            <th scope="col">Run ID</th>
            <th scope="col">Status</th>
            <th scope="col">Started</th>
            <th scope="col">Completed</th>
            <th scope="col">Repository root</th>
            <th scope="col">Solutions</th>
            <th scope="col">Diagnostics</th>
            <th scope="col">Snapshot</th>
            <th scope="col">Action</th>
          </tr>
        </thead>
        <tbody>
          {runs.map((run) => (
            <HistoryRow key={run.runId} isSelected={run.runId === selectedRunId} onSelectRun={onSelectRun} run={run} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Describes inputs required to render one extraction history row.
 */
interface HistoryRowProps {
  /**
   * Contains one compact run summary returned by the history endpoint.
   */
  readonly run: ExtractionRunSummaryResponse;

  /**
   * Indicates whether this row is the currently selected run.
   */
  readonly isSelected: boolean;

  /**
   * Selects the row run for detailed polling.
   */
  readonly onSelectRun: (runId: string) => void;
}

/**
 * Renders one compact extraction history row.
 *
 * @param props Contains the run summary and selection state to render.
 * @param props.run The extraction run summary whose fields should be displayed.
 * @param props.isSelected Indicates whether the row is currently selected.
 * @param props.onSelectRun Receives this row's run identifier when selected.
 * @returns A table row with text-based status, timestamps, counts, and snapshot identity.
 */
function HistoryRow({ run, isSelected, onSelectRun }: HistoryRowProps) {
  // Every state is rendered as text rather than relying on color so the row remains accessible
  // in high-contrast themes and screen readers.
  return (
    <tr aria-selected={isSelected}>
      <th scope="row">{run.runId}</th>
      <td><span className="extraction-history__status-text">{formatStatus(run.status)}</span></td>
      <td>{formatTimestamp(run.startedUtc)}</td>
      <td>{run.completedUtc === null ? 'Not completed' : formatTimestamp(run.completedUtc)}</td>
      <td>{run.repositoryRootDirectory}</td>
      <td>{formatCount(run.solutionCount, 'solution')}</td>
      <td>{formatCount(run.warningCount, 'warning')} · {formatCount(run.errorCount, 'error')}</td>
      <td>{run.snapshotIdentity ?? 'No snapshot yet'}</td>
      <td>
        <Button type="button" variant="outline" size="sm" className="extraction-history__row-action" onClick={() => onSelectRun(run.runId)} aria-label={`Select run ${run.runId} for details`} aria-pressed={isSelected} title={`Load details for ${run.runId}`}>
          {isSelected ? 'Selected' : 'View details'}
        </Button>
      </td>
    </tr>
  );
}

/**
 * Formats a lifecycle status for visible text.
 *
 * @param status The status value returned by the extraction API.
 * @returns A readable status label that preserves API vocabulary without depending on color.
 */
function formatStatus(status: string): string {
  // Status values are already safe API vocabulary. Trimming guards against accidental whitespace
  // while preserving unknown future statuses instead of hiding them from users.
  return status.trim() || 'Unknown';
}

/**
 * Formats a UTC timestamp for compact display.
 *
 * @param timestamp The ISO-like timestamp returned by the extraction API.
 * @returns A stable display value, or the original value when parsing is not possible.
 */
function formatTimestamp(timestamp: string): string {
  // The browser's UTC formatting keeps tests and users independent of local time zones while
  // retaining the original string if the backend later returns a non-ISO safe value.
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return timestamp;
  }

  return date.toISOString().replace('.000Z', 'Z');
}

/**
 * Formats a count with singular or plural noun text.
 *
 * @param count The numeric count returned by the extraction API.
 * @param singularNoun The noun to use when the count is one.
 * @returns A readable count label for tables and tests.
 */
function formatCount(count: number, singularNoun: string): string {
  // Pluralizing small diagnostic/count labels makes table rows readable without adding
  // color-only badges for every numeric value.
  return `${count} ${count === 1 ? singularNoun : `${singularNoun}s`}`;
}
