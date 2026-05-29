import { useQuery } from '@tanstack/react-query';
import { Activity } from 'lucide-react';
import { archonApiClient, type ArchonApiClient } from '@/api/archonApiClient';
import type { ExtractionRunStatusResponse, NormalizedArchonApiError } from '@/api/archonApiTypes';
import { calculateExtractionPollingInterval, deriveExtractionPollingState, isExtractionRunTerminalStatus } from '@/api/polling';
import { archonQueryKeys } from '@/api/queryKeys';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useExtractionCenterStore, type TrackedExtractionRun } from '@/state/extractionCenterStore';

/**
 * Describes the typed API client surface required by the bottom-panel background monitor.
 */
export type ExtractionBackgroundMonitorClient = Pick<ArchonApiClient, 'getExtractionStatus'>;

/**
 * Describes inputs accepted by the background monitor component.
 */
export interface ExtractionBackgroundMonitorProps {
  /**
   * Supplies a production client or deterministic test double for tracked run status reads.
   */
  readonly client?: ExtractionBackgroundMonitorClient;
}

/**
 * Describes the render-only row model used by the bottom-panel monitor view.
 */
export interface ExtractionBackgroundMonitorRunView {
  /**
   * Contains the public run identifier shown and selected by the monitor.
   */
  readonly runId: string;

  /**
   * Contains the safe latest status response when TanStack Query has loaded it.
   */
  readonly status?: ExtractionRunStatusResponse;

  /**
   * Contains a safe normalized polling error when the status read failed.
   */
  readonly error?: NormalizedArchonApiError;

  /**
   * Indicates whether the row is currently fetching status from ArchonApi.
   */
  readonly isFetching: boolean;

  /**
   * Indicates whether the user has acknowledged this terminal row.
   */
  readonly isAcknowledged: boolean;
}

/**
 * Describes render-only inputs for the Extraction Center background monitor view.
 */
export interface ExtractionBackgroundMonitorContentProps {
  /**
   * Provides tracked run rows after query state has been mapped into safe view models.
   */
  readonly runs: readonly ExtractionBackgroundMonitorRunView[];

  /**
   * Selects a tracked run and opens the Extraction Center detail workflow.
   */
  readonly onSelectRun?: (runId: string) => void;

  /**
   * Acknowledges a terminal tracked run so it can leave the visible monitor.
   */
  readonly onAcknowledgeRun?: (runId: string) => void;
}

/**
 * Reads tracked run status through TanStack Query and renders the bottom-panel monitor.
 *
 * @param props Contains the optional typed client override for deterministic tests.
 * @param props.client Optional status client used instead of the production ArchonApi client.
 * @returns A bottom-panel section containing active or terminal extraction run summaries.
 */
export function ExtractionBackgroundMonitor({ client = archonApiClient }: ExtractionBackgroundMonitorProps) {
  // The monitor reads shared local tracking state, but each status payload remains server state owned
  // by a per-run TanStack Query entry that uses the existing extraction run key convention.
  const extractionCenterStore = useExtractionCenterStore();
  const runViews = extractionCenterStore.state.trackedRuns.map((trackedRun) => (
    <TrackedExtractionRunQuery
      client={client}
      key={trackedRun.runId}
      onAcknowledgeRun={extractionCenterStore.acknowledgeRun}
      onSelectRun={extractionCenterStore.selectRun}
      trackedRun={trackedRun}
    />
  ));

  return (
    <section className="workbench-bottom-panel__section workbench-bottom-panel__section--wide" aria-labelledby="extraction-background-monitor-title">
      <div className="workbench-bottom-panel__section-heading">
        <div>
          <h3 id="extraction-background-monitor-title">Extraction Runs</h3>
          <p>Tracked runs remain visible here while you use other workbench activities.</p>
        </div>
        <Badge variant="outline">Background monitor</Badge>
      </div>
      <div className="extraction-background-monitor__rows" role="list" aria-label="Tracked extraction runs">
        {runViews.length === 0 ? <ExtractionBackgroundMonitorEmpty /> : runViews}
      </div>
    </section>
  );
}

/**
 * Reads and renders one tracked run row through its exact query key.
 *
 * @param props Contains tracking metadata, client dependency, and row actions.
 * @param props.client The typed status client used for the current run.
 * @param props.trackedRun The local tracking metadata for the current run identifier.
 * @param props.onSelectRun Callback used to select the row for detail monitoring.
 * @param props.onAcknowledgeRun Callback used to acknowledge terminal rows.
 * @returns A row bound to TanStack Query status state for one tracked run.
 */
function TrackedExtractionRunQuery({ client, trackedRun, onSelectRun, onAcknowledgeRun }: { readonly client: ExtractionBackgroundMonitorClient; readonly trackedRun: TrackedExtractionRun; readonly onSelectRun: (runId: string) => void; readonly onAcknowledgeRun: (runId: string) => void }) {
  // Acknowledged terminal runs remain in local state for notification memory, but the bottom panel
  // hides them so users can clear completed work without deleting query cache entries.
  const query = useQuery<ExtractionRunStatusResponse, NormalizedArchonApiError>({
    queryKey: archonQueryKeys.extraction.run({ runId: trackedRun.runId }),
    retry: false,
    refetchOnWindowFocus: false,
    refetchInterval: (queryState) => {
      // The row keeps active runs fresh even when the main Extraction Center tab is not selected.
      // Terminal states stop polling and remain visible until the user acknowledges them.
      const status = queryState.state.data;
      const pollingState = status === undefined ? 'polling' : deriveExtractionPollingState({ status });
      return pollingState === 'polling' ? calculateExtractionPollingInterval({ attempt: 1 }) : false;
    },
    queryFn: async ({ signal }) => {
      // The typed client owns path construction, cancellation, response parsing, and safe failure
      // shaping, so the bottom panel never calls fetch or builds /extractions routes itself.
      const result = await client.getExtractionStatus(trackedRun.runId, { signal });
      if (!result.ok) {
        throw result.error;
      }

      return result.data;
    },
  });

  if (trackedRun.isAcknowledged) {
    return null;
  }

  return (
    <ExtractionBackgroundMonitorRow
      error={query.error ?? undefined}
      isAcknowledged={trackedRun.isAcknowledged}
      isFetching={query.isFetching}
      onAcknowledgeRun={onAcknowledgeRun}
      onSelectRun={onSelectRun}
      runId={trackedRun.runId}
      status={query.data}
    />
  );
}

/**
 * Renders the background monitor from precomputed view models for focused tests.
 *
 * @param props Contains row view models and optional row action callbacks.
 * @param props.runs The tracked run rows to display.
 * @param props.onSelectRun Optional callback for selecting a row.
 * @param props.onAcknowledgeRun Optional callback for acknowledging terminal rows.
 * @returns A render-only background monitor useful for deterministic unit tests.
 */
export function ExtractionBackgroundMonitorContent({ runs, onSelectRun = () => undefined, onAcknowledgeRun = () => undefined }: ExtractionBackgroundMonitorContentProps) {
  // The content component mirrors the provider-backed monitor but accepts prepared rows so tests can
  // assert safe rendering without creating a query client or provider tree.
  const visibleRuns = runs.filter((run) => !run.isAcknowledged);

  return (
    <section className="workbench-bottom-panel__section workbench-bottom-panel__section--wide" aria-labelledby="extraction-background-monitor-title">
      <div className="workbench-bottom-panel__section-heading">
        <div>
          <h3 id="extraction-background-monitor-title">Extraction Runs</h3>
          <p>Tracked runs remain visible here while you use other workbench activities.</p>
        </div>
        <Badge variant="outline">Background monitor</Badge>
      </div>
      <div className="extraction-background-monitor__rows" role="list" aria-label="Tracked extraction runs">
        {visibleRuns.length === 0
          ? <ExtractionBackgroundMonitorEmpty />
          : visibleRuns.map((run) => (
            <ExtractionBackgroundMonitorRow
              error={run.error}
              isAcknowledged={run.isAcknowledged}
              isFetching={run.isFetching}
              key={run.runId}
              onAcknowledgeRun={onAcknowledgeRun}
              onSelectRun={onSelectRun}
              runId={run.runId}
              status={run.status}
            />
          ))}
      </div>
    </section>
  );
}

/**
 * Renders the no-background-runs state for the monitor.
 *
 * @returns Safe explanatory empty-state copy for the Extraction Runs bottom-panel section.
 */
function ExtractionBackgroundMonitorEmpty() {
  // The empty state is honest about monitoring being available but idle, and avoids inventing run
  // history, worker diagnostics, or API readiness details.
  return (
    <div className="extraction-background-monitor__empty" role="status">
      <Activity aria-hidden="true" size={18} />
      <div>
        <p>No extraction runs are currently tracked.</p>
        <p>Start or select a run in Extraction Center to keep it visible while you navigate elsewhere.</p>
      </div>
    </div>
  );
}

/**
 * Renders one tracked run row with safe status, progress, and actions.
 *
 * @param props Contains the run identity, status/error state, fetch state, and row actions.
 * @returns A keyboard-accessible background monitor row.
 */
function ExtractionBackgroundMonitorRow(props: ExtractionBackgroundMonitorRunView & { readonly onSelectRun: (runId: string) => void; readonly onAcknowledgeRun: (runId: string) => void }) {
  // The row always gives status in words and never relies on badge color alone. Terminal rows expose
  // acknowledgement while active rows remain available for detail selection.
  const statusText = getBackgroundRunStatusText(props.status, props.error);
  const progressText = getBackgroundRunProgressText(props.status, props.error, props.isFetching);
  const isTerminal = props.status === undefined ? props.error !== undefined : isExtractionRunTerminalStatus(props.status.status);

  return (
    <article className="extraction-background-monitor__row" role="listitem" aria-label={`Extraction run ${props.runId} ${statusText}`}>
      <div className="extraction-background-monitor__row-main">
        <span className="extraction-background-monitor__run-id">{props.runId}</span>
        <span>{progressText}</span>
      </div>
      <div className="extraction-background-monitor__row-state" aria-label="Run status">
        <Badge variant={isTerminal ? 'secondary' : 'outline'}>{statusText}</Badge>
        {props.isFetching ? <Badge variant="outline">Refreshing</Badge> : null}
      </div>
      <div className="extraction-background-monitor__row-actions" aria-label={`Actions for extraction run ${props.runId}`}>
        <Button type="button" variant="outline" size="sm" onClick={() => props.onSelectRun(props.runId)}>
          Open run
        </Button>
        {isTerminal ? (
          <Button type="button" variant="ghost" size="sm" onClick={() => props.onAcknowledgeRun(props.runId)}>
            Acknowledge
          </Button>
        ) : null}
      </div>
    </article>
  );
}

/**
 * Produces the safe status text shown for one background run.
 *
 * @param status The latest successful status response, when available.
 * @param error The latest safe status-read error, when available.
 * @returns A human-readable status label that avoids raw backend diagnostics.
 */
function getBackgroundRunStatusText(status?: ExtractionRunStatusResponse, error?: NormalizedArchonApiError): string {
  // Error categories are normalized before reaching the monitor, so the row can state availability
  // without exposing HTTP details or raw exception content.
  if (error !== undefined) {
    return error.category === 'cancelled' ? 'Cancelled' : 'Unavailable';
  }

  return status?.status ?? 'Loading status';
}

/**
 * Produces safe progress copy for one background run.
 *
 * @param status The latest successful status response, when available.
 * @param error The latest safe status-read error, when available.
 * @param isFetching Indicates whether TanStack Query is currently reading this status.
 * @returns A concise progress message suitable for the compact bottom-panel row.
 */
function getBackgroundRunProgressText(status?: ExtractionRunStatusResponse, error?: NormalizedArchonApiError, isFetching = false): string {
  // Progress messages originate from the safe status contract. Error copy intentionally uses a
  // frontend-authored fallback because normalized error messages can belong in durable page UI.
  if (error !== undefined) {
    return 'Status is unavailable. Open the run detail for persistent retry context.';
  }

  if (status === undefined) {
    return isFetching ? 'Reading current status from ArchonApi.' : 'Waiting for status data.';
  }

  const percentageText = status.progress.percentage === null ? '' : ` ${status.progress.percentage}% complete.`;
  return `${status.progress.stage}: ${status.progress.message}${percentageText}`;
}
