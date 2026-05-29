import { Activity, Copy, ExternalLink } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { ExtractionRunPersistenceCountsResponse, ExtractionRunPersistenceDiagnosticsResponse, ExtractionRunStatusResponse, ExtractionRunTimingResponse, NormalizedArchonApiError } from '@/api/archonApiTypes';
import { deriveExtractionPollingState, type ExtractionRunPollingState } from '@/api/polling';

/**
 * Represents the safe polling error shape rendered by the selected-run detail surface.
 */
export type ExtractionRunDetailError = NormalizedArchonApiError;

/**
 * Describes the selected-run detail state supplied by the Extraction Center container.
 */
export interface ExtractionRunDetailProps {
  /**
   * Contains the selected run identifier before or between successful status responses.
   */
  readonly selectedRunId?: string;

  /**
   * Contains the latest polling-backed run status response when one has loaded successfully.
   */
  readonly run?: ExtractionRunStatusResponse;

  /**
   * Contains the safe polling failure when the selected status request could not complete.
   */
  readonly error?: ExtractionRunDetailError;

  /**
   * Indicates whether the first selected-run status request is still in progress.
   */
  readonly isLoading: boolean;

  /**
   * Indicates whether an already loaded selected run is refreshing in the background.
   */
  readonly isRefetching: boolean;

  /**
   * Copies the selected run's submitted request values into the start-extraction form.
   */
  readonly onDuplicateRequest?: () => void;

  /**
   * Explains why duplication is unavailable when selected status lacks required request values.
   */
  readonly duplicateRequestUnavailableReason?: string;

  /**
   * Announces the produced-snapshot handoff placeholder when snapshot context is not implemented yet.
   */
  readonly onOpenProducedSnapshot?: () => void;
}

/**
 * Renders the selected extraction run monitor with polling-aware states and safe diagnostics.
 *
 * @param props Contains the selected run identity, latest status, safe error, and loading flags.
 * @param props.selectedRunId The run identifier currently selected by history or submission.
 * @param props.run The latest successful selected-run status response.
 * @param props.error The safe polling error returned by the shared API foundation.
 * @param props.isLoading Indicates that the first detail request is loading.
 * @param props.isRefetching Indicates that a loaded detail response is refreshing.
 * @returns A selected-run detail section for active, terminal, unavailable, loading, and empty states.
 */
export function ExtractionRunDetail({ selectedRunId, run, error, isLoading, isRefetching, onDuplicateRequest, duplicateRequestUnavailableReason, onOpenProducedSnapshot }: ExtractionRunDetailProps) {
  // The detail surface never owns server state. It renders the state supplied by the polling hook
  // and keeps all diagnostic copy limited to normalized errors and API-approved response fields.
  const normalizedSelectedRunId = selectedRunId?.trim();

  if (normalizedSelectedRunId === undefined || normalizedSelectedRunId.length === 0) {
    return (
      <section aria-labelledby="selected-run-detail-title" className="extraction-run-detail">
        <RunDetailNotice title="Selected run detail" message="Select a history row or submit a new extraction request to monitor one run here." />
      </section>
    );
  }

  if (isLoading && run === undefined) {
    return (
      <section aria-labelledby="selected-run-detail-title" className="extraction-run-detail">
        <RunDetailNotice title="Loading selected run" message={`ArchonExplorer is reading status for run ${normalizedSelectedRunId}.`} />
        <UnavailableRunFollowUpActions reason={duplicateRequestUnavailableReason} />
      </section>
    );
  }

  if (error !== undefined && run === undefined) {
    return (
      <section aria-labelledby="selected-run-detail-title" className="extraction-run-detail">
        <RunDetailErrorNotice error={error} selectedRunId={normalizedSelectedRunId} />
      </section>
    );
  }

  if (run === undefined) {
    return (
      <section aria-labelledby="selected-run-detail-title" className="extraction-run-detail">
        <RunDetailNotice title="Selected run unavailable" message="The selected run cannot be displayed yet. Choose another run or refresh history." />
        <UnavailableRunFollowUpActions reason={duplicateRequestUnavailableReason} />
      </section>
    );
  }

  return <RunDetailLoaded duplicateRequestUnavailableReason={duplicateRequestUnavailableReason} onDuplicateRequest={onDuplicateRequest} onOpenProducedSnapshot={onOpenProducedSnapshot} run={run} isRefetching={isRefetching} />;
}

/**
 * Describes disabled follow-up actions shown before selected status is available.
 */
interface UnavailableRunFollowUpActionsProps {
  /**
   * Explains why selected-run follow-up actions cannot be used yet.
   */
  readonly reason?: string;
}

/**
 * Renders disabled selected-run actions while status detail is absent.
 *
 * @param props Contains the optional safe explanation for disabled actions.
 * @param props.reason Safe user-facing guidance for loading or unavailable selected status.
 * @returns A follow-up action region with disabled controls and explanatory copy.
 */
function UnavailableRunFollowUpActions({ reason }: UnavailableRunFollowUpActionsProps) {
  // Compact history is insufficient for duplication because it does not expose explicit solution
  // path values. Disabled controls make that boundary visible without inventing missing request data.
  return (
    <section aria-labelledby="selected-run-actions-title" className="extraction-run-detail__section extraction-run-detail__actions">
      <h3 id="selected-run-actions-title">Run follow-up actions</h3>
      <div className="extraction-run-detail__action-row">
        <Button type="button" variant="outline" size="sm" disabled aria-disabled="true">
          <Copy aria-hidden="true" size={16} />
          Duplicate request unavailable
        </Button>
        <Button type="button" variant="outline" size="sm" disabled aria-disabled="true">
          <ExternalLink aria-hidden="true" size={16} />
          Open produced snapshot
        </Button>
      </div>
      <p>{reason ?? 'Load selected run detail to duplicate the prior request values safely. History rows only expose compact summaries.'}</p>
    </section>
  );
}

/**
 * Describes the notice state used when no selected-run data is available.
 */
interface RunDetailNoticeProps {
  /**
   * Provides the notice heading.
   */
  readonly title: string;

  /**
   * Provides the safe explanatory message.
   */
  readonly message: string;
}

/**
 * Renders a non-error selected-run notice.
 *
 * @param props Contains the notice heading and body text.
 * @param props.title The visible heading for the state.
 * @param props.message The safe explanatory message for the state.
 * @returns A status region for empty, loading, or unavailable selected-run states.
 */
function RunDetailNotice({ title, message }: RunDetailNoticeProps) {
  // A status region announces state transitions without implying that an error occurred.
  return (
    <div className="extraction-run-detail__notice" role="status">
      <Activity aria-hidden="true" size={20} />
      <div>
        <h2 id="selected-run-detail-title">{title}</h2>
        <p>{message}</p>
      </div>
    </div>
  );
}

/**
 * Describes the selected-run error notice inputs.
 */
interface RunDetailErrorNoticeProps {
  /**
   * Contains the safe normalized polling failure.
   */
  readonly error: ExtractionRunDetailError;

  /**
   * Contains the run identifier that failed to load.
   */
  readonly selectedRunId: string;
}

/**
 * Renders a safe selected-run request failure state.
 *
 * @param props Contains the safe error and selected run identifier.
 * @param props.error The normalized polling failure from the API foundation.
 * @param props.selectedRunId The selected run identifier associated with the failed request.
 * @returns An alert region that distinguishes not-found from unavailable run detail.
 */
function RunDetailErrorNotice({ error, selectedRunId }: RunDetailErrorNoticeProps) {
  // The title separates not-found from general unavailability while the message remains the already
  // sanitized normalized API message supplied by the shared request foundation.
  const title = error.category === 'notFound' ? 'Selected run was not found' : 'Selected run status is unavailable';
  const guidance = error.category === 'notFound'
    ? `Run ${selectedRunId} is not available from ArchonApi. Refresh history or choose another run.`
    : 'The status request failed safely. Retry by selecting the run again or refreshing history after checking API readiness.';

  return (
    <div className="extraction-run-detail__notice extraction-run-detail__notice--error" role="alert">
      <Activity aria-hidden="true" size={20} />
      <div>
        <h2 id="selected-run-detail-title">{title}</h2>
        <p>{error.message}</p>
        <p>{guidance}</p>
        {error.retryable ? <Badge variant="outline">Retry available</Badge> : <Badge variant="warning">Manual attention</Badge>}
      </div>
    </div>
  );
}

/**
 * Describes the loaded selected-run detail inputs.
 */
interface RunDetailLoadedProps {
  /**
   * Contains the selected run status response to display.
   */
  readonly run: ExtractionRunStatusResponse;

  /**
   * Indicates whether the selected run is refreshing in the background.
   */
  readonly isRefetching: boolean;

  /**
   * Copies safely available request values from the selected run into the editable form.
   */
  readonly onDuplicateRequest?: () => void;

  /**
   * Explains why duplicate request cannot be activated for the selected run.
   */
  readonly duplicateRequestUnavailableReason?: string;

  /**
   * Announces the safe produced-snapshot placeholder for completed runs with snapshot identity.
   */
  readonly onOpenProducedSnapshot?: () => void;
}

/**
 * Renders loaded selected-run status, request, progress, timing, snapshot, and diagnostics sections.
 *
 * @param props Contains the selected run and refresh flag.
 * @param props.run The latest selected-run status response.
 * @param props.isRefetching Indicates whether TanStack Query is refreshing this run.
 * @returns A detailed operational monitor for the selected run.
 */
function RunDetailLoaded({ run, isRefetching, onDuplicateRequest, duplicateRequestUnavailableReason, onOpenProducedSnapshot }: RunDetailLoadedProps) {
  // Polling state is derived from the same helper used by the hook so visual language and stop
  // conditions remain aligned with the scheduling behavior.
  const pollingState = deriveExtractionPollingState({ status: run });
  const isTerminal = pollingState !== 'polling' && pollingState !== 'idle' && pollingState !== 'stalled';

  return (
    <section aria-labelledby="selected-run-detail-title" className="extraction-run-detail">
      <div className="extraction-run-detail__heading">
        <div>
          <h2 id="selected-run-detail-title">Selected run detail</h2>
          <p>
            This monitor reads <code>GET /extractions/{'{runId}'}</code> through the typed client and stops automatic polling when the run reaches a terminal status.
          </p>
        </div>
        <div className="extraction-run-detail__badges" aria-label="Selected run state">
          <Badge variant="outline">{formatPollingState(pollingState)}</Badge>
          {isRefetching ? <Badge variant="outline">Refreshing status</Badge> : null}
          {isTerminal ? <Badge variant="secondary">Terminal status</Badge> : <Badge variant="outline">Active monitor</Badge>}
        </div>
      </div>
      <RunIdentitySection run={run} />
      <RunFollowUpActions duplicateRequestUnavailableReason={duplicateRequestUnavailableReason} onDuplicateRequest={onDuplicateRequest} onOpenProducedSnapshot={onOpenProducedSnapshot} run={run} />
      <SubmittedRequestSection run={run} />
      <ProgressSection run={run} pollingState={pollingState} />
      <RunTimings timings={run.timings} title="Top-level timings" emptyMessage="No top-level timing measurements are available yet." />
      <PersistenceDiagnostics diagnostics={run.persistenceDiagnostics} />
      <p className="extraction-run-detail__safe-note">
        Warning and error details are intentionally not fabricated when ArchonApi exposes only counts. Metadata values are omitted; only metadata keys are shown.
      </p>
    </section>
  );
}

/**
 * Describes follow-up actions rendered for the selected run.
 */
interface RunFollowUpActionsProps {
  /**
   * Contains the selected run that may support duplicate or snapshot handoff actions.
   */
  readonly run: ExtractionRunStatusResponse;

  /**
   * Copies the selected run request summary into the start-extraction form when available.
   */
  readonly onDuplicateRequest?: () => void;

  /**
   * Explains why duplicate request is unavailable for this run.
   */
  readonly duplicateRequestUnavailableReason?: string;

  /**
   * Announces the safe produced-snapshot placeholder instead of activating later snapshot context.
   */
  readonly onOpenProducedSnapshot?: () => void;
}

/**
 * Renders duplicate-request and produced-snapshot actions for a selected run.
 *
 * @param props Contains the selected run and optional action callbacks.
 * @param props.run The run whose follow-up actions should be displayed.
 * @param props.onDuplicateRequest Callback that populates the start form without submitting.
 * @param props.duplicateRequestUnavailableReason Safe explanation shown when duplication is disabled.
 * @returns A follow-up action region with safe operational boundaries.
 */
function RunFollowUpActions({ run, onDuplicateRequest, duplicateRequestUnavailableReason, onOpenProducedSnapshot }: RunFollowUpActionsProps) {
  // Actions stay in the detail panel because they depend on selected-run status rather than compact
  // history. The produced-snapshot button is disabled until page-level notification wiring exists.
  const hasProducedSnapshot = run.snapshotIdentity !== null && run.snapshotIdentity.trim().length > 0;
  const duplicateDisabled = onDuplicateRequest === undefined;

  return (
    <section aria-labelledby="selected-run-actions-title" className="extraction-run-detail__section extraction-run-detail__actions">
      <h3 id="selected-run-actions-title">Run follow-up actions</h3>
      <div className="extraction-run-detail__action-row">
        <Button type="button" variant="outline" size="sm" onClick={onDuplicateRequest} disabled={duplicateDisabled} aria-disabled={duplicateDisabled}>
          <Copy aria-hidden="true" size={16} />
          Duplicate request
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={onOpenProducedSnapshot} disabled={!hasProducedSnapshot || onOpenProducedSnapshot === undefined} aria-disabled={!hasProducedSnapshot || onOpenProducedSnapshot === undefined}>
          <ExternalLink aria-hidden="true" size={16} />
          Open produced snapshot
        </Button>
      </div>
      {duplicateDisabled ? <p><strong>Duplicate request unavailable.</strong> {duplicateRequestUnavailableReason ?? 'The selected run has not exposed enough request values yet.'}</p> : <p>Duplicate request copies available repository, solution path, branch, commit, and requested-by values into the form without submitting a new extraction.</p>}
      {hasProducedSnapshot ? (
        <p>Snapshot context is not active yet. WP006 owns opening produced snapshots for dashboards, search, graph views, and lenses. WP006 owns full snapshot context activation. This action does not query graph data, dashboard metrics, search, lenses, or visualizations.</p>
      ) : (
        <p>No produced snapshot is available to open for this run yet.</p>
      )}
    </section>
  );
}

/**
 * Describes a loaded-run subsection input.
 */
interface RunSectionProps {
  /**
   * Contains the selected run response rendered by the subsection.
   */
  readonly run: ExtractionRunStatusResponse;
}

/**
 * Renders core run identity and lifecycle fields.
 *
 * @param props Contains the selected run response.
 * @param props.run The run whose identity and lifecycle fields should be shown.
 * @returns A compact definition-list section for run identity fields.
 */
function RunIdentitySection({ run }: RunSectionProps) {
  // Identity fields stay grouped so users can copy the run ID and understand whether a snapshot was produced.
  return (
    <section aria-labelledby="selected-run-identity-title" className="extraction-run-detail__section">
      <h3 id="selected-run-identity-title">Run identity and lifecycle</h3>
      <dl className="extraction-run-detail__grid">
        <DetailItem label="Run ID" value={run.runId} />
        <DetailItem label="Status" value={formatStatus(run.status)} />
        <DetailItem label="Started" value={formatTimestamp(run.startedUtc)} />
        <DetailItem label="Completed" value={run.completedUtc === null ? 'Not completed' : formatTimestamp(run.completedUtc)} />
        <DetailItem label="Warnings" value={formatCount(run.warningCount, 'warning')} />
        <DetailItem label="Errors" value={formatCount(run.errorCount, 'error')} />
        <DetailItem label="Produced snapshot" value={run.snapshotIdentity ?? 'No snapshot yet'} />
      </dl>
    </section>
  );
}

/**
 * Renders the credential-safe request summary retained for the selected run.
 *
 * @param props Contains the selected run response.
 * @param props.run The run whose submitted request summary should be shown.
 * @returns A section describing repository, solution, source-control, requester, and metadata-key values.
 */
function SubmittedRequestSection({ run }: RunSectionProps) {
  // Request summaries deliberately show metadata keys rather than values so operational context does
  // not turn into a secret or environment-specific diagnostic leak.
  return (
    <section aria-labelledby="selected-run-request-title" className="extraction-run-detail__section">
      <h3 id="selected-run-request-title">Submitted request summary</h3>
      <dl className="extraction-run-detail__grid">
        <DetailItem label="Repository root" value={run.submittedRequest.repositoryRootDirectory} />
        <DetailItem label="Solution paths" value={formatList(run.submittedRequest.solutionPaths, 'No solution paths supplied')} />
        <DetailItem label="Branch" value={run.submittedRequest.branchName ?? 'Not supplied'} />
        <DetailItem label="Commit SHA" value={run.submittedRequest.commitSha ?? 'Not supplied'} />
        <DetailItem label="Requested by" value={run.submittedRequest.requestedBy ?? 'Not supplied'} />
        <DetailItem label="Metadata keys" value={formatList(run.submittedRequest.metadataKeys, 'No metadata keys')} />
      </dl>
    </section>
  );
}

/**
 * Describes progress-section inputs.
 */
interface ProgressSectionProps {
  /**
   * Contains the selected run whose progress should be shown.
   */
  readonly run: ExtractionRunStatusResponse;

  /**
   * Contains the derived polling state associated with the run status.
   */
  readonly pollingState: ExtractionRunPollingState;
}

/**
 * Renders progress stage, message, percentage, and last-updated time.
 *
 * @param props Contains the selected run and derived polling state.
 * @param props.run The run whose progress object should be displayed.
 * @param props.pollingState The derived polling state used for accessible progress text.
 * @returns A progress section using native progress semantics when a percentage exists.
 */
function ProgressSection({ run, pollingState }: ProgressSectionProps) {
  // The percentage is optional in the API contract. When absent, text describes the active stage
  // without inventing a numeric completion value.
  const percentage = run.progress.percentage;
  const progressLabel = percentage === null ? 'Progress percentage unavailable' : `${percentage}% complete`;

  return (
    <section aria-labelledby="selected-run-progress-title" className="extraction-run-detail__section">
      <h3 id="selected-run-progress-title">Progress</h3>
      <dl className="extraction-run-detail__grid">
        <DetailItem label="Polling state" value={formatPollingState(pollingState)} />
        <DetailItem label="Stage" value={run.progress.stage} />
        <DetailItem label="Message" value={run.progress.message} />
        <DetailItem label="Last updated" value={formatTimestamp(run.progress.lastUpdatedUtc)} />
      </dl>
      <div className="extraction-run-detail__progress" aria-label="Selected run progress">
        {percentage === null ? (
          <p>{progressLabel}</p>
        ) : (
          <div role="progressbar" aria-label="Selected run progress percentage" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percentage}>
            <span style={{ inlineSize: `${Math.max(0, Math.min(100, percentage))}%` }} />
          </div>
        )}
        <p>{progressLabel}</p>
      </div>
    </section>
  );
}

/**
 * Describes the timing-list renderer inputs.
 */
interface RunTimingsProps {
  /**
   * Provides the heading for the timing list.
   */
  readonly title: string;

  /**
   * Contains ordered timing measurements returned by ArchonApi.
   */
  readonly timings: readonly ExtractionRunTimingResponse[];

  /**
   * Provides the explanatory message shown when no timings are available.
   */
  readonly emptyMessage: string;
}

/**
 * Renders an ordered timing summary table.
 *
 * @param props Contains title, timing rows, and empty-state text.
 * @param props.title The section heading.
 * @param props.timings The ordered timing measurements to render.
 * @param props.emptyMessage The safe empty-state explanation.
 * @returns A timing table or safe empty-state message.
 */
function RunTimings({ title, timings, emptyMessage }: RunTimingsProps) {
  // Timing stage names are API-controlled display strings. Durations are formatted from numeric
  // milliseconds so the UI does not infer unreported performance detail.
  if (timings.length === 0) {
    return (
      <section aria-labelledby={`${toDomId(title)}-title`} className="extraction-run-detail__section">
        <h3 id={`${toDomId(title)}-title`}>{title}</h3>
        <p>{emptyMessage}</p>
      </section>
    );
  }

  return (
    <section aria-labelledby={`${toDomId(title)}-title`} className="extraction-run-detail__section">
      <h3 id={`${toDomId(title)}-title`}>{title}</h3>
      <div className="extraction-run-detail__table-wrap">
        <table className="extraction-run-detail__table">
          <caption>{title} returned by ArchonApi.</caption>
          <thead>
            <tr>
              <th scope="col">Stage</th>
              <th scope="col">Duration</th>
              <th scope="col">Completed</th>
            </tr>
          </thead>
          <tbody>
            {timings.map((timing) => (
              <tr key={`${timing.stage}-${timing.completedUtc}-${timing.elapsedMilliseconds}`}>
                <th scope="row">{timing.stage}</th>
                <td>{formatDuration(timing.elapsedMilliseconds)}</td>
                <td>{formatTimestamp(timing.completedUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/**
 * Describes persistence diagnostic renderer inputs.
 */
interface PersistenceDiagnosticsProps {
  /**
   * Contains optional persistence diagnostics attached to the run status.
   */
  readonly diagnostics: ExtractionRunPersistenceDiagnosticsResponse | null;
}

/**
 * Renders persistence diagnostics when the API exposes them safely.
 *
 * @param props Contains optional persistence diagnostic data.
 * @param props.diagnostics The persistence diagnostic section or null when not available.
 * @returns A diagnostic breakdown section with counts, timings, or a safe absence explanation.
 */
function PersistenceDiagnostics({ diagnostics }: PersistenceDiagnosticsProps) {
  // Null diagnostics are a meaningful API state for runs that have not reached persistence or older
  // compatibility records, so the UI explains absence instead of treating it as corrupt data.
  if (diagnostics === null) {
    return (
      <section aria-labelledby="selected-run-persistence-title" className="extraction-run-detail__section">
        <h3 id="selected-run-persistence-title">Persistence diagnostics</h3>
        <p>Persistence diagnostics are not available for this run yet. Counts remain limited to the top-level warning and error totals.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="selected-run-persistence-title" className="extraction-run-detail__section">
      <h3 id="selected-run-persistence-title">Persistence diagnostics</h3>
      <p>{diagnostics.completed ? 'The persistence diagnostic set represents a completed persistence attempt.' : 'The persistence diagnostic set may represent partial evidence collected before failure.'}</p>
      <PersistenceCounts counts={diagnostics.counts} />
      <RunTimings title="Persistence timings" timings={diagnostics.timings} emptyMessage="No persistence sub-stage timings are available." />
    </section>
  );
}

/**
 * Describes persistence-count renderer inputs.
 */
interface PersistenceCountsProps {
  /**
   * Contains count measurements returned by the persistence diagnostic section.
   */
  readonly counts: ExtractionRunPersistenceCountsResponse;
}

/**
 * Renders persistence diagnostic counts in a compact definition grid.
 *
 * @param props Contains persistence count values.
 * @param props.counts The count measurements to display.
 * @returns A count summary that distinguishes zero from unmeasured optional values.
 */
function PersistenceCounts({ counts }: PersistenceCountsProps) {
  // Optional null values are rendered as Not measured instead of zero so contributors do not confuse
  // absent measurement with a known empty collection or operation count.
  return (
    <dl className="extraction-run-detail__grid extraction-run-detail__grid--dense">
      <DetailItem label="Repositories" value={String(counts.repositoryCount)} />
      <DetailItem label="Solutions" value={String(counts.solutionCount)} />
      <DetailItem label="Projects" value={String(counts.projectCount)} />
      <DetailItem label="Files" value={String(counts.fileCount)} />
      <DetailItem label="Nodes" value={String(counts.nodeCount)} />
      <DetailItem label="Relationships" value={String(counts.relationshipCount)} />
      <DetailItem label="Evidence" value={String(counts.evidenceCount)} />
      <DetailItem label="Findings" value={String(counts.findingCount)} />
      <DetailItem label="Persistence warnings" value={String(counts.warningCount)} />
      <DetailItem label="Persistence errors" value={String(counts.errorCount)} />
      <DetailItem label="Metrics" value={String(counts.metricCount)} />
      <DetailItem label="Generated summaries" value={String(counts.generatedSummaryCount)} />
      <DetailItem label="Metadata entries" value={formatOptionalCount(counts.metadataEntryCount)} />
      <DetailItem label="Persistence operations" value={formatOptionalCount(counts.persistenceOperationCount)} />
      <DetailItem label="Persistence batches" value={formatOptionalCount(counts.persistenceBatchCount)} />
      <DetailItem label="Serialized payload bytes" value={formatOptionalCount(counts.serializedPayloadBytes)} />
    </dl>
  );
}

/**
 * Describes one label/value pair in selected-run detail sections.
 */
interface DetailItemProps {
  /**
   * Provides the human-readable field label.
   */
  readonly label: string;

  /**
   * Provides the already-safe display value.
   */
  readonly value: string;
}

/**
 * Renders one selected-run detail value.
 *
 * @param props Contains the field label and display value.
 * @param props.label The visible field label.
 * @param props.value The safe display value.
 * @returns A definition-list item pair for a selected-run attribute.
 */
function DetailItem({ label, value }: DetailItemProps) {
  // Definition-list fields keep labels and values associated for assistive technologies and make
  // compact operational fields easier to scan.
  return (
    <div className="extraction-run-detail__item">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

/**
 * Formats a lifecycle status for visible text.
 *
 * @param status The status value returned by the extraction API.
 * @returns A readable status label that preserves API vocabulary without depending on color.
 */
function formatStatus(status: string): string {
  // Empty status text becomes Unknown so incomplete future responses do not render blank badges.
  return status.trim() || 'Unknown';
}

/**
 * Formats a polling state for visible text.
 *
 * @param state The normalized polling state derived from the selected run.
 * @returns A readable polling state label.
 */
function formatPollingState(state: ExtractionRunPollingState): string {
  // The state is converted from machine-readable lower-case tokens to visible copy while preserving
  // cancelled/canceled behavior in the helper rather than the component.
  const words = state.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/-/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * Formats a UTC timestamp for compact display.
 *
 * @param timestamp The ISO-like timestamp returned by the extraction API.
 * @returns A stable display value, or the original value when parsing is not possible.
 */
function formatTimestamp(timestamp: string): string {
  // ISO output avoids locale drift in tests while preserving the backend string when parsing fails.
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
 * @returns A readable count label for operational summaries.
 */
function formatCount(count: number, singularNoun: string): string {
  // Textual counts avoid color-only diagnostics and do not imply individual diagnostic details exist.
  return `${count} ${count === 1 ? singularNoun : `${singularNoun}s`}`;
}

/**
 * Formats a duration in milliseconds into compact seconds or millisecond text.
 *
 * @param elapsedMilliseconds The elapsed duration reported by ArchonApi.
 * @returns A readable duration value.
 */
function formatDuration(elapsedMilliseconds: number): string {
  // Milliseconds remain visible for short operations, while longer durations get a seconds helper for scanning.
  if (elapsedMilliseconds < 1_000) {
    return `${elapsedMilliseconds} ms`;
  }

  return `${elapsedMilliseconds} ms (${(elapsedMilliseconds / 1_000).toFixed(1)} s)`;
}

/**
 * Formats an optional count, distinguishing null from zero.
 *
 * @param count The optional count returned by persistence diagnostics.
 * @returns The count as text, or Not measured when the API reported null.
 */
function formatOptionalCount(count: number | null): string {
  // Null means the writer did not safely or cheaply measure the value; it is not equivalent to zero.
  return count === null ? 'Not measured' : String(count);
}

/**
 * Formats a list of safe values for a compact definition-list cell.
 *
 * @param values The safe values returned by ArchonApi.
 * @param emptyText The text to show when the list is empty.
 * @returns A comma-separated value list or the supplied empty text.
 */
function formatList(values: readonly string[], emptyText: string): string {
  // Joining with commas keeps request summaries compact without hiding how many explicit solution
  // paths or metadata keys the API retained.
  return values.length === 0 ? emptyText : values.join(', ');
}

/**
 * Converts a heading into a stable DOM identifier fragment.
 *
 * @param value The heading text that needs an identifier.
 * @returns A lower-case identifier fragment safe for local section ids.
 */
function toDomId(value: string): string {
  // The helper is intentionally small because titles are frontend-authored constants, not user input.
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
}
