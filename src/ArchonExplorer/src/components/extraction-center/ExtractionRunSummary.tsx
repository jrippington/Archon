import { Activity } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { ExtractionRunStatusResponse } from '@/api/archonApiTypes';

/**
 * Describes the accepted-run summary input.
 */
export interface ExtractionRunSummaryProps {
  /**
   * Contains the accepted or selected extraction run status returned by ArchonApi.
   */
  readonly run?: ExtractionRunStatusResponse;
}

/**
 * Renders the currently accepted extraction run summary.
 *
 * @param props Contains the optional accepted run to display.
 * @param props.run The accepted run status response returned by POST /extractions.
 * @returns A safe summary region for the selected accepted run or an explanatory empty state.
 */
export function ExtractionRunSummary({ run }: ExtractionRunSummaryProps) {
  // Work Item 2 only displays the accepted run returned by the start request. Later slices replace
  // or extend this surface with polling-backed selected-run detail without fabricating diagnostics.
  if (run === undefined) {
    return (
      <section aria-labelledby="accepted-run-title" className="extraction-run-summary">
        <div className="extraction-run-summary__notice" role="status">
          <Activity aria-hidden="true" size={20} />
          <div>
            <h2 id="accepted-run-title">Accepted run</h2>
            <p>Submit an extraction request to see the accepted run identity and current status here.</p>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section aria-labelledby="accepted-run-title" className="extraction-run-summary">
      <div className="extraction-run-summary__heading">
        <div>
          <h2 id="accepted-run-title">Accepted run</h2>
          <p>The API accepted this extraction request. Detailed polling arrives in the next Extraction Center slice.</p>
        </div>
        <Badge variant="outline">{formatStatus(run.status)}</Badge>
      </div>
      <dl className="extraction-run-summary__grid">
        <SummaryItem label="Run ID" value={run.runId} />
        <SummaryItem label="Started" value={formatTimestamp(run.startedUtc)} />
        <SummaryItem label="Progress stage" value={run.progress.stage} />
        <SummaryItem label="Progress message" value={run.progress.message} />
        <SummaryItem label="Warnings" value={formatCount(run.warningCount, 'warning')} />
        <SummaryItem label="Errors" value={formatCount(run.errorCount, 'error')} />
        <SummaryItem label="Snapshot" value={run.snapshotIdentity ?? 'No snapshot yet'} />
      </dl>
      <p className="extraction-run-summary__safe-note">
        Warning and error counts are shown only as counts until the API exposes safe diagnostic detail for this UI.
      </p>
    </section>
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
