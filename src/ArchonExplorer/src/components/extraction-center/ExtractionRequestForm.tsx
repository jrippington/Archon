import type { FormEvent, ReactNode, RefObject } from 'react';
import { Activity, Plus, Trash2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { ApiConnectivityState } from '@/api/connectivity';
import type { StartExtractionError } from '@/hooks/useStartExtraction';
import type { ExtractionRequestFormState, ExtractionRequestFormValidationMessages } from './extractionFormState';

/**
 * Describes the props required by the extraction request form.
 */
export interface ExtractionRequestFormProps {
  /**
   * Contains the current browser-owned form values.
   */
  readonly state: ExtractionRequestFormState;

  /**
   * Contains current client-side or server-side validation messages keyed by field.
   */
  readonly validationMessages: ExtractionRequestFormValidationMessages;

  /**
   * Contains the latest safe submission error when one exists.
   */
  readonly submissionError?: StartExtractionError;

  /**
   * Contains safe API readiness information used to guard submission.
   */
  readonly connectivityState: ApiConnectivityState;

  /**
   * Indicates whether POST /extractions is currently in flight.
   */
  readonly isSubmitting: boolean;

  /**
   * Contains safe duplication guidance after request values are copied from selected status.
   */
  readonly duplicateNotice?: string;

  /**
   * Provides the focusable persistent summary target used after duplicate population.
   */
  readonly formSummaryRef?: RefObject<HTMLDivElement | null>;

  /**
   * Replaces the entire form state after a field edit.
   */
  readonly onStateChange: (state: ExtractionRequestFormState) => void;

  /**
   * Requests validation and submission of the current form state.
   */
  readonly onSubmit: () => void;
}

/**
 * Renders the form used to submit a new extraction request.
 *
 * @param props Contains form state, validation, connectivity, and submission callbacks.
 * @param props.state The current editable form values.
 * @param props.validationMessages Safe messages to render near invalid fields.
 * @param props.submissionError The latest safe submission failure.
 * @param props.connectivityState Safe API readiness state for guarding submit.
 * @param props.isSubmitting Indicates whether a submit mutation is in progress.
 * @param props.onStateChange Receives updated form state after user edits.
 * @param props.onSubmit Requests form validation and mutation.
 * @returns An accessible start-extraction form.
 */
export function ExtractionRequestForm({ state, validationMessages, submissionError, connectivityState, isSubmitting, duplicateNotice, formSummaryRef, onStateChange, onSubmit }: ExtractionRequestFormProps) {
  // The form remains controlled by the parent component so validation failures and mutation errors
  // preserve every entered value for correction.
  const isApiConfigured = connectivityState.status !== 'unconfigured';
  const canSubmit = isApiConfigured && !isSubmitting;

  /**
   * Handles the native submit event without allowing browser page navigation.
   *
   * @param event The form submission event raised by the browser.
   */
  function handleSubmit(event: FormEvent<HTMLFormElement>): void {
    // The React callback delegates validation and mutation to the feature container while keeping
    // the form itself focused on accessible input rendering.
    event.preventDefault();
    onSubmit();
  }

  return (
    <section aria-labelledby="start-extraction-title" className="extraction-request-form">
      <div className="extraction-request-form__heading">
        <div>
          <h2 id="start-extraction-title">Start extraction</h2>
          <p>
            Submit an explicit repository root and one or more explicit solution paths. ArchonExplorer does not recursively discover solution files.
          </p>
        </div>
        <Badge variant={connectivityState.status === 'reachable' ? 'secondary' : 'outline'}>{connectivityState.label}</Badge>
      </div>
      <form className="extraction-request-form__form" onSubmit={handleSubmit} noValidate>
        <SubmissionFeedback validationMessages={validationMessages} submissionError={submissionError} connectivityState={connectivityState} duplicateNotice={duplicateNotice} formSummaryRef={formSummaryRef} />
        <FieldGroup fieldId="repository-root-directory" label="Repository root directory" messages={validationMessages.repositoryRootDirectory}>
          <input
            id="repository-root-directory"
            name="repositoryRootDirectory"
            type="text"
            value={state.repositoryRootDirectory}
            onChange={(event) => onStateChange({ ...state, repositoryRootDirectory: event.currentTarget.value })}
            aria-describedby="repository-root-directory-description repository-root-directory-errors"
            aria-invalid={validationMessages.repositoryRootDirectory !== undefined}
            autoComplete="off"
          />
          <p id="repository-root-directory-description" className="extraction-request-form__help">
            Use the repository root directory that contains the submitted solution files. Server validation remains authoritative.
          </p>
        </FieldGroup>
        <fieldset className="extraction-request-form__fieldset" aria-describedby="solution-paths-description solution-paths-errors">
          <legend>Explicit solution paths</legend>
          <p id="solution-paths-description" className="extraction-request-form__help">
            Relative solution paths resolve against the submitted repository root. Add every solution intentionally; recursive scanning is not performed.
          </p>
          <div className="extraction-request-form__solution-list">
            {state.solutionPaths.map((solutionPath, index) => (
              <SolutionPathRow
                key={`solution-path-${index}`}
                index={index}
                value={solutionPath}
                canRemove={state.solutionPaths.length > 1}
                onChange={(value) => onStateChange(replaceSolutionPath(state, index, value))}
                onRemove={() => onStateChange(removeSolutionPath(state, index))}
              />
            ))}
          </div>
          <Button type="button" variant="outline" size="sm" onClick={() => onStateChange(addSolutionPath(state))}>
            <Plus aria-hidden="true" size={16} />
            Add solution path
          </Button>
          <FieldMessages fieldId="solution-paths" messages={validationMessages.solutionPaths} />
        </fieldset>
        <div className="extraction-request-form__optional-grid">
          <FieldGroup fieldId="branch-name" label="Branch name" messages={validationMessages.branchName}>
            <input
              id="branch-name"
              name="branchName"
              type="text"
              value={state.branchName}
              onChange={(event) => onStateChange({ ...state, branchName: event.currentTarget.value })}
              aria-describedby="branch-name-errors"
              aria-invalid={validationMessages.branchName !== undefined}
              autoComplete="off"
            />
          </FieldGroup>
          <FieldGroup fieldId="commit-sha" label="Commit SHA" messages={validationMessages.commitSha}>
            <input
              id="commit-sha"
              name="commitSha"
              type="text"
              value={state.commitSha}
              onChange={(event) => onStateChange({ ...state, commitSha: event.currentTarget.value })}
              aria-describedby="commit-sha-errors"
              aria-invalid={validationMessages.commitSha !== undefined}
              autoComplete="off"
            />
          </FieldGroup>
          <FieldGroup fieldId="requested-by" label="Requested by" messages={validationMessages.requestedBy}>
            <input
              id="requested-by"
              name="requestedBy"
              type="text"
              value={state.requestedBy}
              onChange={(event) => onStateChange({ ...state, requestedBy: event.currentTarget.value })}
              aria-describedby="requested-by-errors"
              aria-invalid={validationMessages.requestedBy !== undefined}
              autoComplete="off"
            />
          </FieldGroup>
        </div>
        <FieldGroup fieldId="metadata-text" label="Metadata" messages={validationMessages.metadata}>
          <textarea
            id="metadata-text"
            name="metadata"
            value={state.metadataText}
            onChange={(event) => onStateChange({ ...state, metadataText: event.currentTarget.value })}
            aria-describedby="metadata-text-description metadata-text-errors"
            aria-invalid={validationMessages.metadata !== undefined}
            rows={4}
          />
          <p id="metadata-text-description" className="extraction-request-form__help">
            Optional metadata uses one key=value entry per line. Status responses expose metadata keys only.
          </p>
        </FieldGroup>
        <div className="extraction-request-form__actions">
          <Button type="submit" disabled={isSubmitting} aria-disabled={!canSubmit}>
            {isSubmitting ? 'Submitting extraction' : 'Submit extraction'}
          </Button>
          {!canSubmit && !isSubmitting ? <p>Submission is guarded until the API is configured for browser requests.</p> : null}
        </div>
      </form>
    </section>
  );
}

/**
 * Describes props used to render a labeled field group.
 */
interface FieldGroupProps {
  /**
   * Provides the stable DOM identifier for the editable control.
   */
  readonly fieldId: string;

  /**
   * Provides the visible label text.
   */
  readonly label: string;

  /**
   * Contains optional field-level messages.
   */
  readonly messages?: readonly string[];

  /**
   * Contains the input and supporting help content.
   */
  readonly children: ReactNode;
}

/**
 * Renders a label, control, and field-level validation messages.
 *
 * @param props Contains field label, identifier, messages, and child controls.
 * @param props.fieldId The stable DOM identifier for label association.
 * @param props.label The visible field label.
 * @param props.messages Optional field-level safe validation messages.
 * @param props.children The input control and help content.
 * @returns A grouped form field with accessible label and messages.
 */
function FieldGroup({ fieldId, label, messages, children }: FieldGroupProps) {
  // Each field gets a predictable error element identifier so aria-describedby can include it even
  // when there are currently no messages.
  return (
    <div className="extraction-request-form__field">
      <label htmlFor={fieldId}>{label}</label>
      {children}
      <FieldMessages fieldId={fieldId} messages={messages} />
    </div>
  );
}

/**
 * Describes props used to render field-level message lists.
 */
interface FieldMessagesProps {
  /**
   * Provides the field identifier used to create the error container id.
   */
  readonly fieldId: string;

  /**
   * Contains safe field messages when validation failed.
   */
  readonly messages?: readonly string[];
}

/**
 * Renders safe validation messages for one form field.
 *
 * @param props Contains the field id and optional messages.
 * @param props.fieldId The field id used for the message container id.
 * @param props.messages Safe messages to render.
 * @returns A validation list, or an empty described element when no messages exist.
 */
function FieldMessages({ fieldId, messages }: FieldMessagesProps) {
  // Empty message containers keep aria-describedby targets stable without adding visible noise.
  if (messages === undefined || messages.length === 0) {
    return <div id={`${fieldId}-errors`} className="extraction-request-form__errors" />;
  }

  return (
    <ul id={`${fieldId}-errors`} className="extraction-request-form__errors">
      {messages.map((message) => (
        <li key={message}>{message}</li>
      ))}
    </ul>
  );
}

/**
 * Describes props for one solution path row.
 */
interface SolutionPathRowProps {
  /**
   * Contains the zero-based row index for labels and callbacks.
   */
  readonly index: number;

  /**
   * Contains the current solution path text.
   */
  readonly value: string;

  /**
   * Indicates whether the remove action should be enabled.
   */
  readonly canRemove: boolean;

  /**
   * Receives the edited solution path value.
   */
  readonly onChange: (value: string) => void;

  /**
   * Removes this row from the form state.
   */
  readonly onRemove: () => void;
}

/**
 * Renders one keyboard-operable solution path row.
 *
 * @param props Contains row index, value, and edit/remove callbacks.
 * @param props.index The zero-based row index.
 * @param props.value The current solution path value.
 * @param props.canRemove Indicates whether the row can be removed.
 * @param props.onChange Receives edited row text.
 * @param props.onRemove Removes the row.
 * @returns A labeled solution path input with an optional remove button.
 */
function SolutionPathRow({ index, value, canRemove, onChange, onRemove }: SolutionPathRowProps) {
  // A visually explicit row label helps users understand that each row is one submitted solution,
  // not a search pattern or directory to scan.
  const fieldId = `solution-path-${index}`;
  return (
    <div className="extraction-request-form__solution-row">
      <label htmlFor={fieldId}>Solution path {index + 1}</label>
      <input
        id={fieldId}
        name="solutionPaths"
        type="text"
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
        autoComplete="off"
      />
      <Button type="button" variant="outline" size="sm" onClick={onRemove} disabled={!canRemove} aria-disabled={!canRemove} aria-label={`Remove solution path ${index + 1}`}>
        <Trash2 aria-hidden="true" size={16} />
        Remove
      </Button>
    </div>
  );
}

/**
 * Describes props for the safe submission feedback region.
 */
interface SubmissionFeedbackProps {
  /**
   * Contains current field or form validation messages.
   */
  readonly validationMessages: ExtractionRequestFormValidationMessages;

  /**
   * Contains the latest safe submission error when available.
   */
  readonly submissionError?: StartExtractionError;

  /**
   * Contains safe API readiness status for setup feedback.
   */
  readonly connectivityState: ApiConnectivityState;

  /**
   * Contains safe duplicate-request guidance when metadata values or other values need review.
   */
  readonly duplicateNotice?: string;

  /**
   * Provides the focusable target used by duplicate-request actions.
   */
  readonly formSummaryRef?: RefObject<HTMLDivElement | null>;
}

/**
 * Renders persistent safe submission and setup feedback.
 *
 * @param props Contains validation messages, submission errors, and API readiness state.
 * @param props.validationMessages Current safe validation messages.
 * @param props.submissionError Latest safe mutation error.
 * @param props.connectivityState Safe API connectivity state.
 * @returns A persistent feedback region when form-level feedback is needed.
 */
function SubmissionFeedback({ validationMessages, submissionError, connectivityState, duplicateNotice, formSummaryRef }: SubmissionFeedbackProps) {
  // Persistent feedback remains in the page because validation and setup problems require durable
  // correction context even if a future notification also announces the failure.
  const formMessages = validationMessages.form ?? [];
  const shouldShowConnectivity = connectivityState.status === 'unconfigured';
  const shouldShowFeedback = formMessages.length > 0 || submissionError !== undefined || shouldShowConnectivity || duplicateNotice !== undefined;

  if (!shouldShowFeedback) {
    return null;
  }

  return (
    <div className="extraction-request-form__feedback" role={submissionError === undefined ? 'status' : 'alert'} tabIndex={-1} ref={formSummaryRef}>
      <Activity aria-hidden="true" size={20} />
      <div>
        <h3>Submission needs attention</h3>
        {shouldShowConnectivity ? <p>{connectivityState.description ?? connectivityState.label}</p> : null}
        {submissionError !== undefined ? <p>{submissionError.message}</p> : null}
        {duplicateNotice !== undefined ? <p>{duplicateNotice}</p> : null}
        {formMessages.length > 0 ? (
          <ul>
            {formMessages.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        ) : null}
      </div>
    </div>
  );
}

/**
 * Adds one blank explicit solution path row.
 *
 * @param state The current form state.
 * @returns Updated form state containing a new blank row.
 */
function addSolutionPath(state: ExtractionRequestFormState): ExtractionRequestFormState {
  // Adding rows never infers paths; it only gives the user another explicit input slot.
  return { ...state, solutionPaths: [...state.solutionPaths, ''] };
}

/**
 * Replaces one explicit solution path row.
 *
 * @param state The current form state.
 * @param index The zero-based row index to replace.
 * @param value The new row text.
 * @returns Updated form state with one row changed.
 */
function replaceSolutionPath(state: ExtractionRequestFormState, index: number, value: string): ExtractionRequestFormState {
  // The map preserves row order so the submitted solution list follows the user's visible order.
  return { ...state, solutionPaths: state.solutionPaths.map((solutionPath, currentIndex) => (currentIndex === index ? value : solutionPath)) };
}

/**
 * Removes one explicit solution path row while preserving at least one row.
 *
 * @param state The current form state.
 * @param index The zero-based row index to remove.
 * @returns Updated form state after removal.
 */
function removeSolutionPath(state: ExtractionRequestFormState, index: number): ExtractionRequestFormState {
  // The UI keeps one blank row available so users are never left without an editable solution slot.
  const remainingRows = state.solutionPaths.filter((_, currentIndex) => currentIndex !== index);
  return { ...state, solutionPaths: remainingRows.length === 0 ? [''] : remainingRows };
}
