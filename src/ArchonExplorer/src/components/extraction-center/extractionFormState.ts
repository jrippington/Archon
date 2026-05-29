import type { ExtractionRunStatusResponse, NormalizedValidationIssue, StartExtractionRequest } from '@/api/archonApiTypes';

/**
 * Names every editable field or field group in the start-extraction form.
 */
export type ExtractionRequestFormField =
  | 'repositoryRootDirectory'
  | 'solutionPaths'
  | 'branchName'
  | 'commitSha'
  | 'requestedBy'
  | 'metadata'
  | 'form';

/**
 * Describes validation messages keyed by start-extraction form field.
 */
export type ExtractionRequestFormValidationMessages = Partial<Record<ExtractionRequestFormField, readonly string[]>>;

/**
 * Stores browser-owned input values for a start-extraction request before it becomes server state.
 */
export interface ExtractionRequestFormState {
  /**
   * Contains the user-entered repository root directory value exactly as the form should preserve it.
   */
  readonly repositoryRootDirectory: string;

  /**
   * Contains one or more user-editable explicit solution path rows.
   */
  readonly solutionPaths: readonly string[];

  /**
   * Contains optional source-control branch context for the submitted run.
   */
  readonly branchName: string;

  /**
   * Contains optional source-control commit context for the submitted run.
   */
  readonly commitSha: string;

  /**
   * Contains optional actor context for the submitted run.
   */
  readonly requestedBy: string;

  /**
   * Contains optional metadata as newline-delimited key=value entries before request mapping.
   */
  readonly metadataText: string;
}

/**
 * Describes the outcome of validating and mapping form state into an API request.
 */
export type ExtractionRequestFormMappingResult =
  | {
      /**
       * Indicates that form state was valid enough for client-side submission.
       */
      readonly ok: true;

      /**
       * Contains the normalized JSON body sent to POST /extractions.
       */
      readonly request: StartExtractionRequest;
    }
  | {
      /**
       * Indicates that convenience validation found missing or malformed form input.
       */
      readonly ok: false;

      /**
       * Contains safe field-level messages that should be rendered without losing entered values.
       */
      readonly validationMessages: ExtractionRequestFormValidationMessages;
    };

/**
 * Describes the safe result of reconstructing an editable request from selected run status.
 */
export type DuplicateExtractionRequestFormResult =
  | {
      /**
       * Indicates that the selected run exposed enough request values to repopulate the form.
       */
      readonly ok: true;

      /**
       * Contains the editable form values copied from the selected run status response.
       */
      readonly formState: ExtractionRequestFormState;

      /**
       * Contains safe guidance for any copied request fields that need user attention.
       */
      readonly validationMessages: ExtractionRequestFormValidationMessages;

      /**
       * Lists metadata keys that were present but whose values were intentionally unavailable.
       */
      readonly omittedMetadataKeys: readonly string[];
    }
  | {
      /**
       * Indicates that the selected run cannot be duplicated without user re-entry.
       */
      readonly ok: false;

      /**
       * Provides safe user-facing guidance explaining why duplication is unavailable.
       */
      readonly reason: string;

      /**
       * Contains field-level guidance for values that must be re-entered manually.
       */
      readonly validationMessages: ExtractionRequestFormValidationMessages;
    };

/**
 * Provides the safe persistent message shown when metadata values cannot be duplicated.
 */
const metadataValuesUnavailableMessage = 'Metadata values are not exposed by run status and must be re-entered before submitting if needed. Metadata keys from the previous request are shown only as safe context.';

/**
 * Provides the safe persistent message shown when compact or incomplete status cannot be duplicated.
 */
const duplicateRequestUnavailableMessage = 'The selected run does not expose enough submitted request values to duplicate it safely. Re-enter the repository root directory and explicit solution paths before submitting a new extraction.';

/**
 * Creates the initial start-extraction form state.
 *
 * @returns A form state with one empty explicit solution-path row and blank optional context fields.
 */
export function createInitialExtractionRequestFormState(): ExtractionRequestFormState {
  // The first empty solution row keeps keyboard flow simple and makes the explicit-solution-path
  // contract visible before the user adds additional rows.
  return {
    repositoryRootDirectory: '',
    solutionPaths: [''],
    branchName: '',
    commitSha: '',
    requestedBy: '',
    metadataText: '',
  };
}

/**
 * Maps selected run status into editable form state for a duplicate request workflow.
 *
 * @param run The selected run status response that may contain the previous submitted request summary.
 * @returns A populated form state when required values are present, or safe guidance when re-entry is required.
 */
export function mapRunStatusToDuplicateFormState(run: ExtractionRunStatusResponse): DuplicateExtractionRequestFormResult {
  // Duplication starts from selected status instead of compact history because status contains the
  // accepted request summary. The browser still refuses to infer or discover solution paths.
  const repositoryRootDirectory = run.submittedRequest.repositoryRootDirectory.trim();
  const solutionPaths = normalizeSolutionPaths(run.submittedRequest.solutionPaths);
  const validationMessages: ExtractionRequestFormValidationMessages = {};

  if (repositoryRootDirectory.length === 0) {
    validationMessages.repositoryRootDirectory = ['Re-enter the repository root directory because the selected run did not expose it.'];
  }

  if (solutionPaths.length === 0) {
    validationMessages.solutionPaths = ['Re-enter at least one explicit solution path because the selected run did not expose solution path values.'];
  }

  if (validationMessages.repositoryRootDirectory !== undefined || validationMessages.solutionPaths !== undefined) {
    return {
      ok: false,
      reason: duplicateRequestUnavailableMessage,
      validationMessages,
    };
  }

  const omittedMetadataKeys = run.submittedRequest.metadataKeys.filter((metadataKey) => metadataKey.trim().length > 0);
  if (omittedMetadataKeys.length > 0) {
    validationMessages.form = [metadataValuesUnavailableMessage];
  }

  return {
    ok: true,
    formState: {
      repositoryRootDirectory,
      solutionPaths,
      branchName: run.submittedRequest.branchName ?? '',
      commitSha: run.submittedRequest.commitSha ?? '',
      requestedBy: run.submittedRequest.requestedBy ?? '',
      metadataText: '',
    },
    validationMessages,
    omittedMetadataKeys,
  };
}

/**
 * Trims a form value and converts empty text into undefined for optional request properties.
 *
 * @param value The raw form value to normalize.
 * @returns The trimmed non-empty value, or undefined when the input is blank.
 */
export function normalizeOptionalText(value: string): string | undefined {
  // Undefined optional properties are omitted from the JSON body by the request executor's
  // ordinary JSON serialization, which keeps empty strings from becoming accidental metadata.
  const trimmed = value.trim();
  return trimmed.length === 0 ? undefined : trimmed;
}

/**
 * Trims solution path rows and removes blank rows before request submission.
 *
 * @param solutionPaths The editable solution path rows from the form.
 * @returns The non-empty explicit solution paths in their submitted order.
 */
export function normalizeSolutionPaths(solutionPaths: readonly string[]): readonly string[] {
  // The browser does not resolve paths or check the filesystem. It only removes accidental
  // whitespace and empty rows so the server remains authoritative for repository boundaries.
  return solutionPaths.map((solutionPath) => solutionPath.trim()).filter((solutionPath) => solutionPath.length > 0);
}

/**
 * Parses newline-delimited metadata text into a request metadata object.
 *
 * @param metadataText The raw metadata text where each non-empty line should be shaped as key=value.
 * @returns A metadata object when entries exist, undefined when metadata is blank, or validation messages when parsing fails.
 */
export function parseMetadataText(metadataText: string): { readonly metadata?: Record<string, string>; readonly validationMessages?: readonly string[] } {
  // Metadata is intentionally simple for this slice: one key=value entry per line. Values may
  // contain additional equals signs, but keys must be present so the API receives deterministic names.
  const metadata: Record<string, string> = {};
  const messages: string[] = [];
  const lines = metadataText.split(/\r?\n/);

  lines.forEach((line, index) => {
    // Blank lines are ignored so users can group entries visually without creating empty metadata.
    const trimmedLine = line.trim();
    if (trimmedLine.length === 0) {
      return;
    }

    const separatorIndex = trimmedLine.indexOf('=');
    if (separatorIndex <= 0) {
      messages.push(`Metadata line ${index + 1} must use key=value format.`);
      return;
    }

    const key = trimmedLine.slice(0, separatorIndex).trim();
    const value = trimmedLine.slice(separatorIndex + 1).trim();
    if (key.length === 0) {
      messages.push(`Metadata line ${index + 1} must include a key before the equals sign.`);
      return;
    }

    metadata[key] = value;
  });

  if (messages.length > 0) {
    return { validationMessages: messages };
  }

  return Object.keys(metadata).length === 0 ? {} : { metadata };
}

/**
 * Validates start-extraction form state for obvious missing browser-side values.
 *
 * @param state The current browser-owned form state.
 * @returns Safe validation messages keyed by field; an empty object means the state can be submitted.
 */
export function validateExtractionRequestFormState(state: ExtractionRequestFormState): ExtractionRequestFormValidationMessages {
  // Client-side validation stays deliberately shallow. The API remains authoritative for existence,
  // extension, duplicate, and inside-repository checks because the browser cannot inspect local paths.
  const messages: ExtractionRequestFormValidationMessages = {};

  if (state.repositoryRootDirectory.trim().length === 0) {
    messages.repositoryRootDirectory = ['Enter the repository root directory to extract.'];
  }

  if (normalizeSolutionPaths(state.solutionPaths).length === 0) {
    messages.solutionPaths = ['Enter at least one explicit solution path. ArchonExplorer does not discover solutions recursively.'];
  }

  const metadataParseResult = parseMetadataText(state.metadataText);
  if (metadataParseResult.validationMessages !== undefined) {
    messages.metadata = metadataParseResult.validationMessages;
  }

  return messages;
}

/**
 * Maps validated form state into the typed POST /extractions request body.
 *
 * @param state The current browser-owned form state.
 * @returns Either a normalized request body or safe validation messages that prevent submission.
 */
export function mapExtractionRequestFormStateToRequest(state: ExtractionRequestFormState): ExtractionRequestFormMappingResult {
  // Mapping is the last browser-side step before mutation. Keeping it pure makes tests cover the
  // exact request body without rendering React components or calling the network.
  const validationMessages = validateExtractionRequestFormState(state);
  if (Object.keys(validationMessages).length > 0) {
    return { ok: false, validationMessages };
  }

  const metadataParseResult = parseMetadataText(state.metadataText);
  return {
    ok: true,
    request: {
      repositoryRootDirectory: state.repositoryRootDirectory.trim(),
      solutionPaths: normalizeSolutionPaths(state.solutionPaths),
      branchName: normalizeOptionalText(state.branchName),
      commitSha: normalizeOptionalText(state.commitSha),
      requestedBy: normalizeOptionalText(state.requestedBy),
      metadata: metadataParseResult.metadata,
    },
  };
}

/**
 * Converts normalized server validation issues into start-extraction form messages.
 *
 * @param issues The normalized validation issues returned by the API request foundation.
 * @returns Field-level messages suitable for rendering beside the current preserved form values.
 */
export function mapServerValidationIssuesToFormMessages(issues: readonly NormalizedValidationIssue[] | undefined): ExtractionRequestFormValidationMessages {
  // Server field names can differ by casing or use stable validation-code buckets. Known fields are
  // routed to their form controls, while unknown safe issues remain visible in the form summary.
  const messages: ExtractionRequestFormValidationMessages = {};
  if (issues === undefined) {
    return messages;
  }

  for (const issue of issues) {
    const field = mapServerFieldName(issue.field);
    messages[field] = [...(messages[field] ?? []), ...issue.messages];
  }

  return messages;
}

/**
 * Maps one server validation field name into a browser form field bucket.
 *
 * @param fieldName The normalized server field name or validation code.
 * @returns The matching form field bucket, or form when no specific control owns the issue.
 */
function mapServerFieldName(fieldName: string): ExtractionRequestFormField {
  // Matching lower-cased field text handles common ASP.NET Core casing differences without
  // trusting unknown server field names as DOM identifiers or UI structure.
  const normalized = fieldName.trim().toLowerCase();
  if (normalized.includes('repositoryrootdirectory')) {
    return 'repositoryRootDirectory';
  }

  if (normalized.includes('solutionpaths') || normalized.includes('solutionpath')) {
    return 'solutionPaths';
  }

  if (normalized.includes('branchname')) {
    return 'branchName';
  }

  if (normalized.includes('commitsha')) {
    return 'commitSha';
  }

  if (normalized.includes('requestedby')) {
    return 'requestedBy';
  }

  if (normalized.includes('metadata')) {
    return 'metadata';
  }

  return 'form';
}
