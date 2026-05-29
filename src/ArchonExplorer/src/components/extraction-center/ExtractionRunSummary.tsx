import { Badge } from '@/components/ui/badge';
import type { ExtractionRunStatusResponse } from '@/api/archonApiTypes';
import { deriveExtractionPollingState, type ExtractionRunPollingState } from '@/api/polling';
import type { ExtractionRunDetailError } from '@/components/extraction-center/ExtractionRunDetail';

/**
 * Describes the accepted-run summary input.
 */
export interface ExtractionRunSummaryProps {
  /**
   * Contains the accepted or polling-backed extraction run status returned by ArchonApi.
   */
  readonly run?: ExtractionRunStatusResponse;

  /**
   * Contains the selected or accepted run identifier before a polling response is available.
   */
  readonly runId?: string;

  /**
   * Contains the normalized safe polling failure for the compact update status region.
   */
  readonly error?: ExtractionRunDetailError;

  /**
   * Indicates whether submission or status polling is actively refreshing the surface.
   */
  readonly isRefreshing?: boolean;
}

/**
 * Renders the compact Snapshot update status surface.
 *
 * @param props Contains the optional accepted or selected run plus safe polling state.
 * @param props.run The accepted or selected run status response returned by ArchonApi.
 * @param props.runId The selected or accepted run identifier available before a status response exists.
 * @param props.error The safe polling failure to summarize without raw diagnostics.
 * @param props.isRefreshing Indicates whether submission or polling is currently refreshing.
 * @returns A safe compact status region for active, terminal, unavailable, unknown, and empty states.
 */
export function ExtractionRunSummary({ run, runId, error, isRefreshing = false }: ExtractionRunSummaryProps) {
  // The update surface is intentionally small: it summarizes the same accepted/polling state used by
  // the durable detail pane without introducing a log console, event stream, or copied server cache.
  const updateState = deriveSnapshotUpdateState({ error, isRefreshing, run });
  if (run === undefined && error === undefined) {
    return (
      <section aria-labelledby="snapshot-update-status-title" aria-live="polite" className="extraction-run-summary" role="status">
        <div className="extraction-run-summary__notice" role="status">
          <div>
            <h2 id="snapshot-update-status-title">Update status</h2>
            <p>{runId === undefined ? 'No active update.' : `Waiting for status for ${runId}.`}</p>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section aria-labelledby="snapshot-update-status-title" aria-live="polite" className="extraction-run-summary" role="status">
      <div className="extraction-run-summary__heading">
        <div>
          <h2 id="snapshot-update-status-title">Update status</h2>
          <p title="Status shows lifecycle, stage, message, aggregate warning and error counts, and snapshot identity when ArchonApi returns those safe fields.">{formatUpdateSummary({ error, run, runId, updateState })}</p>
        </div>
        <div className="extraction-run-summary__badges" aria-label="Snapshot update state">
          <Badge variant={updateState.badgeVariant}>{updateState.label}</Badge>
          {isRefreshing ? <Badge variant="outline">Refreshing</Badge> : null}
        </div>
      </div>
      {run === undefined ? <UnavailableSummary error={error} runId={runId} /> : <LoadedSummary run={run} />}
      <p className="extraction-run-summary__safe-note">
        Counts only. Details stay out of status.
      </p>
    </section>
  );
}

/**
 * Describes the inputs used to derive compact Snapshot update state.
 */
interface DeriveSnapshotUpdateStateOptions {
  /**
   * Contains the latest accepted or selected run status when one is available.
   */
  readonly run?: ExtractionRunStatusResponse;

  /**
   * Contains the safe polling error when the status request failed.
   */
  readonly error?: ExtractionRunDetailError;

  /**
   * Indicates whether submission or polling is actively refreshing.
   */
  readonly isRefreshing: boolean;
}

/**
 * Describes the visible state label and compact badge treatment for Snapshot update status.
 */
interface SnapshotUpdateStateDisplay {
  /**
   * Contains the visible state text that does not rely on color.
   */
  readonly label: string;

  /**
   * Selects a standard badge treatment from the local shadcn-compatible primitive.
   */
  readonly badgeVariant: 'default' | 'secondary' | 'outline' | 'warning';

  /**
   * Contains the normalized polling state that drove the display label.
   */
  readonly pollingState: ExtractionRunPollingState;
}

/**
 * Derives compact visible Snapshot update state from existing polling helpers.
 *
 * @param options Contains accepted or selected run data, safe error, and refresh state.
 * @returns The text label and badge treatment for the compact update status header.
 */
function deriveSnapshotUpdateState({ run, error, isRefreshing }: DeriveSnapshotUpdateStateOptions): SnapshotUpdateStateDisplay {
  // The same polling helper owns terminal and unavailable classification, preventing the compact
  // status strip from drifting away from TanStack Query scheduling and selected-run detail behavior.
  const pollingState = deriveExtractionPollingState({ status: run, error });
  if (error !== undefined) {
    return { label: 'Unavailable', badgeVariant: 'warning', pollingState };
  }

  if (run === undefined) {
    return { label: isRefreshing ? 'Queued' : 'No active update', badgeVariant: 'outline', pollingState: 'idle' };
  }

  const status = run.status.trim().toLowerCase();
  if (status === 'queued') {
    return { label: 'Queued', badgeVariant: 'outline', pollingState };
  }

  if (pollingState === 'polling') {
    return { label: 'Running', badgeVariant: 'default', pollingState };
  }

  if (pollingState === 'completed') {
    return { label: 'Completed', badgeVariant: 'secondary', pollingState };
  }

  if (pollingState === 'failed') {
    return { label: 'Failed', badgeVariant: 'warning', pollingState };
  }

  if (pollingState === 'canceled' || pollingState === 'cancelled') {
    return { label: 'Cancelled', badgeVariant: 'warning', pollingState };
  }

  if (pollingState === 'unavailable') {
    return { label: 'Unavailable', badgeVariant: 'warning', pollingState };
  }

  if (pollingState === 'stalled') {
    return { label: 'Stalled', badgeVariant: 'warning', pollingState };
  }

  return { label: 'Unknown', badgeVariant: 'outline', pollingState };
}

/**
 * Describes inputs needed to build the compact status summary sentence.
 */
interface FormatUpdateSummaryOptions {
  /**
   * Contains the latest accepted or selected run status when one is available.
   */
  readonly run?: ExtractionRunStatusResponse;

  /**
   * Contains the selected or accepted run identifier when known.
   */
  readonly runId?: string;

  /**
   * Contains a safe polling failure when status is unavailable.
   */
  readonly error?: ExtractionRunDetailError;

  /**
   * Contains the derived display state for the status header.
   */
  readonly updateState: SnapshotUpdateStateDisplay;
}

/**
 * Formats one terse status summary sentence for the update region.
 *
 * @param options Contains the run, selected identity, error, and derived state to summarize.
 * @returns A safe one-line operational summary.
 */
function formatUpdateSummary({ run, runId, error, updateState }: FormatUpdateSummaryOptions): string {
  // The summary names the run and high-level state only. Detailed retry context remains in selected
  // run detail, while this surface avoids raw server messages or verbose diagnostic streams.
  const identity = run?.runId ?? runId;
  if (error !== undefined) {
    return identity === undefined ? 'Unavailable.' : `Unavailable: ${identity}.`;
  }

  if (run === undefined) {
    return identity === undefined ? 'No active update.' : `Waiting: ${identity}.`;
  }

  return `${updateState.label}: ${run.runId}.`;
}

/**
 * Renders available API progress and output facts for a loaded update status response.
 *
 * @param props Contains the loaded run status to display.
 * @param props.run The accepted or selected status response returned by ArchonApi.
 * @returns A compact definition list of safe update status facts.
 */
function LoadedSummary({ run }: { readonly run: ExtractionRunStatusResponse }) {
  // The list contains only API-approved status fields and aggregate counts. It intentionally avoids
  // rendering timings, persistence diagnostics, metadata values, or arbitrary backend text here.
  return (
    <dl className="extraction-run-summary__grid">
      <SummaryItem label="Run" value={run.runId} />
      <SummaryItem label="Status" value={formatStatus(run.status)} />
      <SummaryItem label="Stage" value={formatOptionalText(run.progress.stage, 'No stage yet')} />
      <SummaryItem label="Message" value={formatOptionalText(run.progress.message, 'No message yet')} />
      <SummaryItem label="Warnings" value={formatCount(run.warningCount, 'warning')} />
      <SummaryItem label="Errors" value={formatCount(run.errorCount, 'error')} />
      <SummaryItem label="Snapshot" value={run.snapshotIdentity ?? 'No snapshot yet'} />
    </dl>
  );
}

/**
 * Renders safe unavailable status detail when polling fails before a loaded response exists.
 *
 * @param props Contains safe error and optional run identity values.
 * @param props.error The normalized polling error produced by the API foundation.
 * @param props.runId The selected or accepted run identity associated with the failed read.
 * @returns A compact definition list with safe unavailable status facts.
 */
function UnavailableSummary({ error, runId }: { readonly error?: ExtractionRunDetailError; readonly runId?: string }) {
  // The error message has already been normalized, but this compact region still keeps retry and
  // raw diagnostic detail out of the status strip; durable retry context stays in selected detail.
  return (
    <dl className="extraction-run-summary__grid extraction-run-summary__grid--compact">
      <SummaryItem label="Run" value={runId ?? 'No run selected'} />
      <SummaryItem label="Status" value="Unavailable" />
      <SummaryItem label="Message" value={error?.message ?? 'Status is not available yet.'} />
    </dl>
  );
}

/**
 * Describes one label/value pair in the accepted-run summary.
 */
interface SummaryItemProps {
  /**
   * Provides the human-readable field label.
   */
  readonly label: string;

  /**
   * Provides the already-formatted field value.
   */
  readonly value: string;
}

/**
 * Formats optional API text with a terse fallback.
 *
 * @param value The raw stage or message text returned by ArchonApi.
 * @param fallback The text shown when the API field is empty.
 * @returns A trimmed field value or the supplied fallback.
 */
function formatOptionalText(value: string, fallback: string): string {
  // Empty stage or message values are treated as absent facts instead of hidden layout gaps.
  const trimmed = value.trim();
  return trimmed.length === 0 ? fallback : trimmed;
}

/**
 * Renders one accepted-run summary field.
 *
 * @param props Contains the field label and value to display.
 * @param props.label The human-readable field label.
 * @param props.value The safe field value.
 * @returns A definition-list item pair for a single run attribute.
 */
function SummaryItem({ label, value }: SummaryItemProps) {
  // Definition-list markup keeps compact operational fields associated for assistive technology.
  return (
    <div className="extraction-run-summary__item">
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
  // Status values are controlled API vocabulary. Empty or whitespace-only values become Unknown
  // so the UI remains honest when a future backend response is incomplete.
  return status.trim() || 'Unknown';
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
