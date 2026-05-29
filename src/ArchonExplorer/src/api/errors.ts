import type {
  ArchonApiErrorCategory,
  NormalizedArchonApiError,
  NormalizedValidationIssue,
  ProblemDetailsResponse,
  SafeQueryErrorResponse,
  ValidationProblemDetailsResponse,
} from './archonApiTypes';

/**
 * Lists text fragments that must never appear in user-visible diagnostics because
 * they usually indicate stack traces, secrets, database internals, or raw query text.
 */
const unsafeDiagnosticFragments = [
  'password=',
  'pwd=',
  'user id=',
  'connectionstring',
  'connection string',
  'stacktrace',
  '--- end of stack trace',
  'system.',
  'microsoft.',
  'neo4j',
  'cypher',
  'bearer ',
  'token=',
  'apikey',
  'api_key',
  'secret=',
  ' at ',
] as const;

/**
 * Supplies stable safe fallback messages for every normalized error category.
 */
const defaultMessages: Record<ArchonApiErrorCategory, string> = {
  configuration: 'Archon API is not configured. Set the API base URL before retrying.',
  network: 'Archon API could not be reached. Check that the service is running and accessible.',
  timeout: 'Archon API did not respond before the request timed out.',
  validation: 'The request could not be accepted because one or more values need attention.',
  notFound: 'The requested Archon API resource was not found.',
  conflict: 'The request conflicts with the current Archon API state.',
  server: 'Archon API could not complete the request.',
  unexpectedResponse: 'Archon API returned a response the workbench could not read safely.',
  cancelled: 'The request was cancelled.',
  unknown: 'An unexpected API client error occurred.',
};

/**
 * Describes the HTTP-response evidence needed to normalize a failed request.
 */
export interface ShapeHttpErrorOptions {
  /**
   * Carries the HTTP status code returned by ArchonApi.
   */
  readonly status: number;

  /**
   * Carries the parsed JSON body when parsing succeeded and the content was JSON.
   */
  readonly body?: unknown;

  /**
   * Carries the route or URL path for future diagnostics without exposing query values.
   */
  readonly route?: string;
}

/**
 * Describes safe overrides for constructing a normalized error directly.
 */
export interface CreateNormalizedErrorOptions {
  /**
   * Selects the safe high-level category for the failure.
   */
  readonly category: ArchonApiErrorCategory;

  /**
   * Provides a candidate message that will be redacted if it contains unsafe fragments.
   */
  readonly message?: string;

  /**
   * Carries an optional HTTP status code associated with the failure.
   */
  readonly status?: number;

  /**
   * Carries a safe machine-readable code when one is available.
   */
  readonly code?: string;

  /**
   * Carries a safe support correlation identifier when one is available.
   */
  readonly traceIdentifier?: string;

  /**
   * Carries safe validation issues for validation failures.
   */
  readonly validationIssues?: readonly NormalizedValidationIssue[];

  /**
   * Overrides default retry guidance when a caller knows retry would be unsafe.
   */
  readonly retryable?: boolean;
}

/**
 * Determines whether a thrown value represents browser-native cancellation.
 *
 * @param error - The unknown thrown value caught by the request layer.
 * @returns True when the value looks like an abort or cancellation signal.
 */
export function isCancellationError(error: unknown): boolean {
  // Browser fetch rejects aborted requests with a DOMException named AbortError;
  // tests and some runtimes may use Error instances with equivalent names.
  return error instanceof DOMException && error.name === 'AbortError'
    || error instanceof Error && (error.name === 'AbortError' || error.name === 'CanceledError');
}

/**
 * Determines whether a thrown value represents request timeout cancellation.
 *
 * @param error - The unknown thrown value caught by the request layer.
 * @returns True when the value was created by the timeout helper.
 */
export function isTimeoutError(error: unknown): boolean {
  // The request executor marks its timeout abort reason with TimeoutError so
  // user-initiated cancellation and client-enforced timeout can be reported separately.
  return error instanceof DOMException && error.name === 'TimeoutError'
    || error instanceof Error && error.name === 'TimeoutError';
}

/**
 * Redacts a candidate diagnostic unless it is short, non-empty, and free from
 * known unsafe implementation fragments.
 *
 * @param value - The optional backend or runtime text to evaluate for UI use.
 * @param fallback - The safe fallback text used when the candidate fails closed.
 * @returns The candidate text when safe, otherwise the fallback text.
 */
export function sanitizeDiagnosticMessage(value: unknown, fallback: string): string {
  // The frontend intentionally fails closed because it cannot prove arbitrary
  // exception text from a backend, browser, proxy, or extension is safe to display.
  if (typeof value !== 'string') {
    return fallback;
  }

  const trimmed = value.trim();
  if (trimmed.length === 0 || trimmed.length > 240) {
    return fallback;
  }

  const normalized = trimmed.toLowerCase();
  if (unsafeDiagnosticFragments.some((fragment) => normalized.includes(fragment))) {
    return fallback;
  }

  return trimmed;
}

/**
 * Creates a normalized frontend error with safe default messaging and retry
 * guidance.
 *
 * @param options - The category and optional safe metadata used to build the error.
 * @returns A fail-closed normalized error suitable for UI state and notifications.
 */
export function createNormalizedError(options: CreateNormalizedErrorOptions): NormalizedArchonApiError {
  // Retry defaults are conservative: only transient categories opt in, and callers
  // can disable retry for destructive operations or other non-idempotent workflows.
  const retryable = options.retryable ?? (options.category === 'network' || options.category === 'timeout' || options.category === 'server');
  const fallback = defaultMessages[options.category];

  return {
    category: options.category,
    message: sanitizeDiagnosticMessage(options.message, fallback),
    status: options.status,
    code: sanitizeCode(options.code),
    traceIdentifier: sanitizeTraceIdentifier(options.traceIdentifier),
    validationIssues: options.validationIssues,
    retryable,
  };
}

/**
 * Converts a failed HTTP response into a normalized frontend error.
 *
 * @param options - The HTTP status and optional parsed body evidence from the response.
 * @returns A safe normalized error that does not expose raw backend diagnostics.
 */
export function shapeHttpError(options: ShapeHttpErrorOptions): NormalizedArchonApiError {
  // Validation and documented query envelopes get first chance because they carry
  // structured safe information; otherwise status-code classes map to generic fallbacks.
  if (isValidationProblem(options.body, options.status)) {
    return shapeValidationProblem(options.body, options.status);
  }

  if (isSafeQueryError(options.body)) {
    return shapeSafeQueryError(options.body, options.status);
  }

  const problem = isProblemDetails(options.body) ? options.body : undefined;
  const category = mapStatusToCategory(options.status);
  const problemTraceIdentifier = problem?.traceIdentifier ?? problem?.traceId;

  return createNormalizedError({
    category,
    message: problem?.title ?? problem?.detail,
    status: options.status,
    traceIdentifier: problemTraceIdentifier,
  });
}

/**
 * Converts a validation-problem response into safe form and field errors.
 *
 * @param problem - The validation problem body parsed from JSON.
 * @param status - The HTTP status code that carried the validation response.
 * @returns A normalized validation error with safe issues and support metadata.
 */
export function shapeValidationProblem(problem: ValidationProblemDetailsResponse, status = problem.status ?? 400): NormalizedArchonApiError {
  // Field messages are sanitized independently so one unsafe backend message does
  // not force the entire validation response to be discarded.
  const validationIssues = Object.entries(problem.errors ?? {}).map(([field, messages]) => ({
    field: sanitizeCode(field) ?? 'form',
    messages: messages.map((message) => sanitizeDiagnosticMessage(message, defaultMessages.validation)),
  }));

  return createNormalizedError({
    category: 'validation',
    message: problem.title ?? defaultMessages.validation,
    status,
    traceIdentifier: problem.traceIdentifier ?? problem.traceId,
    validationIssues,
    retryable: false,
  });
}

/**
 * Converts a documented safe query error envelope into a normalized frontend
 * error.
 *
 * @param error - The safe query error envelope parsed from JSON.
 * @param status - The HTTP status code that carried the query error.
 * @returns A normalized error preserving only safe code, message, and trace metadata.
 */
export function shapeSafeQueryError(error: SafeQueryErrorResponse, status: number): NormalizedArchonApiError {
  // Query errors are intentionally safe envelopes; their message is still passed
  // through the same redaction gate so the frontend remains defensive if a server regresses.
  return createNormalizedError({
    category: mapStatusToCategory(status),
    message: error.message,
    status,
    code: error.code,
    traceIdentifier: error.traceIdentifier ?? undefined,
  });
}

/**
 * Converts a thrown request-layer failure into a normalized frontend error.
 *
 * @param error - The unknown thrown value from fetch, URL construction, or parsing.
 * @returns A safe normalized error for cancellation, timeout, network, or unknown failures.
 */
export function shapeThrownError(error: unknown): NormalizedArchonApiError {
  // Browser and test runtime errors are never displayed directly. Only the error
  // class is used to choose a safe category and stable fallback message.
  if (isTimeoutError(error)) {
    return createNormalizedError({ category: 'timeout' });
  }

  if (isCancellationError(error)) {
    return createNormalizedError({ category: 'cancelled', retryable: false });
  }

  if (error instanceof TypeError) {
    return createNormalizedError({ category: 'network' });
  }

  return createNormalizedError({ category: 'unknown' });
}

/**
 * Maps HTTP status codes to normalized frontend categories.
 *
 * @param status - The HTTP status code returned by the API or a proxy.
 * @returns The closest safe frontend error category.
 */
export function mapStatusToCategory(status: number): ArchonApiErrorCategory {
  // Status mapping keeps the UI vocabulary stable even if ASP.NET Core returns
  // different problem-detail titles for the same status class.
  if (status === 400 || status === 422) {
    return 'validation';
  }

  if (status === 404) {
    return 'notFound';
  }

  if (status === 409) {
    return 'conflict';
  }

  if (status >= 500) {
    return 'server';
  }

  return 'unexpectedResponse';
}

/**
 * Determines whether a parsed JSON body has the documented safe query error shape.
 *
 * @param value - The parsed JSON value to inspect.
 * @returns True when the value contains the stable query error fields.
 */
function isSafeQueryError(value: unknown): value is SafeQueryErrorResponse {
  // The check is intentionally structural because query endpoints return plain JSON.
  return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string';
}

/**
 * Determines whether a parsed JSON body is an ASP.NET Core validation problem.
 *
 * @param value - The parsed JSON value to inspect.
 * @param status - The response status associated with the parsed value.
 * @returns True when the body and status match validation-problem conventions.
 */
function isValidationProblem(value: unknown, status: number): value is ValidationProblemDetailsResponse {
  // Validation responses normally have an errors dictionary and 400 or 422 status.
  return isRecord(value) && isRecord(value.errors) && (status === 400 || status === 422);
}

/**
 * Determines whether a parsed JSON body is a problem-details object.
 *
 * @param value - The parsed JSON value to inspect.
 * @returns True when the value looks like a problem-details response.
 */
function isProblemDetails(value: unknown): value is ProblemDetailsResponse {
  // ProblemDetails fields are optional, so the presence of common textual fields is
  // enough to distinguish it from arbitrary JSON success data after an HTTP failure.
  return isRecord(value) && (
    typeof value.title === 'string'
    || typeof value.detail === 'string'
    || typeof value.traceId === 'string'
    || typeof value.traceIdentifier === 'string'
  );
}

/**
 * Determines whether an unknown value can be inspected as a string-keyed object.
 *
 * @param value - The unknown value to inspect.
 * @returns True when the value is a non-null object record.
 */
function isRecord(value: unknown): value is Record<string, unknown> {
  // Arrays are objects but not meaningful problem or query-error envelopes here.
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * Sanitizes machine-readable codes and field names before preserving them.
 *
 * @param value - The optional code or field name supplied by the server.
 * @returns The safe code or undefined when the value is empty or unsafe.
 */
function sanitizeCode(value: string | undefined): string | undefined {
  // Codes should be compact identifiers; if they contain unsafe text, omit them
  // rather than attempting to present a partially redacted identifier.
  if (value === undefined) {
    return undefined;
  }

  const sanitized = sanitizeDiagnosticMessage(value, '');
  return sanitized.length > 0 ? sanitized : undefined;
}

/**
 * Sanitizes support trace identifiers before preserving them.
 *
 * @param value - The optional trace identifier supplied by the server.
 * @returns The safe trace identifier or undefined when absent or unsafe.
 */
function sanitizeTraceIdentifier(value: string | undefined): string | undefined {
  // Trace identifiers are useful for support but still pass through the diagnostic
  // redaction gate so a malformed backend cannot smuggle secret text into metadata.
  if (value === undefined) {
    return undefined;
  }

  const sanitized = sanitizeDiagnosticMessage(value, '');
  return sanitized.length > 0 ? sanitized : undefined;
}
