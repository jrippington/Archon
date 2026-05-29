import type { ApiConfiguration } from '@/config/apiConfiguration';
import { getApiConfiguration } from '@/config/apiConfiguration';
import type { ArchonApiPath } from './archonApiRoutes';
import type { NormalizedArchonApiError } from './archonApiTypes';
import { createNormalizedError, shapeHttpError, shapeThrownError } from './errors';

/**
 * Describes primitive query-string values accepted by the request executor.
 */
export type ArchonApiQueryValue = string | number | boolean | null | undefined;

/**
 * Describes typed query-string objects serialized by the request executor.
 */
export type ArchonApiQuery = Readonly<Record<string, ArchonApiQueryValue | readonly ArchonApiQueryValue[]>>;

/**
 * Names the HTTP methods used by the browser-side ArchonApi runtime.
 */
export type ArchonApiRequestMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

/**
 * Describes the fetch-compatible function shape used by production code and tests.
 */
export type ArchonFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/**
 * Describes one browser request handled by the ArchonApi request executor.
 */
export interface ArchonApiRequestOptions<TBody = unknown> {
  /**
   * Selects the HTTP method used for the request.
   */
  readonly method: ArchonApiRequestMethod;

  /**
   * Supplies the no-common-prefix ArchonApi route path to call.
   */
  readonly path: ArchonApiPath;

  /**
   * Supplies typed query-string values to serialize after the base path.
   */
  readonly query?: ArchonApiQuery;

  /**
   * Supplies an optional JSON request body for methods that accept a body.
   */
  readonly body?: TBody;

  /**
   * Allows callers or TanStack Query to cancel the request when the consumer unmounts.
   */
  readonly signal?: AbortSignal;

  /**
   * Applies timeout-compatible abort behavior without requiring callers to create their own timer.
   */
  readonly timeoutMs?: number;
}

/**
 * Describes dependencies that can be replaced by tests without changing request
 * execution semantics.
 */
export interface ArchonApiRequestExecutorOptions {
  /**
   * Reads the current API configuration; production uses the Vite-backed configuration helper.
   */
  readonly getConfiguration?: () => ApiConfiguration;

  /**
   * Executes the browser request; production uses global fetch.
   */
  readonly fetch?: ArchonFetch;
}

/**
 * Represents a successful request result or a safe normalized failure.
 */
export type ArchonApiRequestResult<TResponse> =
  | {
      /**
       * Indicates that the request completed successfully and data is safe to consume.
       */
      readonly ok: true;

      /**
       * Contains the parsed success data, or undefined for intentionally empty responses.
       */
      readonly data: TResponse;

      /**
       * Carries the HTTP status code associated with the success response.
       */
      readonly status: number;
    }
  | {
      /**
       * Indicates that the request failed and only the normalized error should be shown.
       */
      readonly ok: false;

      /**
       * Contains a safe frontend error that has already been redacted and classified.
       */
      readonly error: NormalizedArchonApiError;

      /**
       * Carries the HTTP status code when the failure came from a response.
       */
      readonly status?: number;
    };

/**
 * Executes ArchonApi requests with base URL resolution, query serialization,
 * JSON handling, cancellation, timeout behavior, and safe error shaping.
 */
export class ArchonApiRequestExecutor {
  /**
   * Reads API configuration for each request so runtime environment changes can be reflected.
   */
  private readonly getConfiguration: () => ApiConfiguration;

  /**
   * Executes the browser request through production fetch or a test double.
   */
  private readonly fetch: ArchonFetch;

  /**
   * Initializes a new request executor.
   *
   * @param options - Optional dependency overrides used by tests or specialized hosts.
   */
  public constructor(options: ArchonApiRequestExecutorOptions = {}) {
    // Dependencies are captured once while configuration itself is read per request,
    // which keeps tests deterministic and production behavior aligned with Vite config.
    this.getConfiguration = options.getConfiguration ?? getApiConfiguration;
    this.fetch = options.fetch ?? globalThis.fetch.bind(globalThis);
  }

  /**
   * Executes one typed ArchonApi request and returns either parsed data or a safe error.
   *
   * @param options - The route, method, query, body, cancellation, and timeout settings.
   * @returns A success result with typed data or a failure result with normalized diagnostics.
   */
  public async execute<TResponse = void, TBody = unknown>(options: ArchonApiRequestOptions<TBody>): Promise<ArchonApiRequestResult<TResponse>> {
    // The request flow resolves configuration, builds the absolute URL, prepares JSON
    // request metadata, then parses or normalizes the response without throwing raw diagnostics.
    const configuration = this.getConfiguration();
    if (!configuration.isConfigured || configuration.baseUrl === undefined) {
      return {
        ok: false,
        error: createNormalizedError({ category: 'configuration', retryable: false }),
      };
    }

    let timeoutHandle: ReturnType<typeof setTimeout> | undefined;
    const abortController = new AbortController();
    const abortForwarder = createAbortForwarder(options.signal, abortController);

    try {
      const url = buildArchonApiUrl(configuration.baseUrl, options.path, options.query);
      if (options.timeoutMs !== undefined) {
        timeoutHandle = setTimeout(() => abortController.abort(new DOMException('The request timed out.', 'TimeoutError')), options.timeoutMs);
      }

      const response = await this.fetch(url, {
        method: options.method,
        headers: createRequestHeaders(options.body),
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: abortController.signal,
      });

      return await parseResponse<TResponse>(response);
    } catch (error) {
      return {
        ok: false,
        error: shapeThrownError(abortController.signal.reason ?? error),
      };
    } finally {
      // Timers and signal listeners must always be cleaned up so cancelled or completed
      // requests do not keep stale callbacks alive in long-running workbench sessions.
      if (timeoutHandle !== undefined) {
        clearTimeout(timeoutHandle);
      }

      abortForwarder.dispose();
    }
  }
}

/**
 * Shared default request executor used by near-term API client wrappers.
 */
export const archonApiRequestExecutor = new ArchonApiRequestExecutor();

/**
 * Convenience wrapper for callers that do not need a custom executor instance.
 *
 * @param options - The route, method, query, body, cancellation, and timeout settings.
 * @returns A success result with typed data or a failure result with normalized diagnostics.
 */
export function executeArchonApiRequest<TResponse = void, TBody = unknown>(options: ArchonApiRequestOptions<TBody>): Promise<ArchonApiRequestResult<TResponse>> {
  // The function keeps simple call sites terse while still routing through the
  // documented executor object that tests and future clients can replace directly.
  return archonApiRequestExecutor.execute<TResponse, TBody>(options);
}

/**
 * Builds an absolute ArchonApi URL from the configured base URL, a route path, and
 * typed query parameters.
 *
 * @param baseUrl - The configured ArchonApi base URL from Vite runtime configuration.
 * @param path - The route-catalog path that intentionally has no common /api prefix.
 * @param query - Optional typed query-string values to append to the request URL.
 * @returns An absolute URL object ready for fetch.
 */
export function buildArchonApiUrl(baseUrl: string, path: ArchonApiPath, query?: ArchonApiQuery): URL {
  // URL construction delegates path joining to the platform so callers cannot
  // accidentally concatenate duplicated slashes or manually inject query delimiters.
  const normalizedBaseUrl = baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`;
  const url = new URL(path.replace(/^\//u, ''), normalizedBaseUrl);

  appendQuery(url, query);
  return url;
}

/**
 * Appends typed query-string values to an absolute URL.
 *
 * @param url - The URL object that receives serialized query values.
 * @param query - The optional query object supplied by a typed API method.
 */
function appendQuery(url: URL, query?: ArchonApiQuery): void {
  // Undefined and null values are omitted so optional filters do not become literal
  // strings; arrays repeat the same key, matching common ASP.NET Core query binding.
  if (query === undefined) {
    return;
  }

  for (const [key, value] of Object.entries(query)) {
    if (isQueryValueArray(value)) {
      for (const item of value) {
        appendQueryValue(url, key, item);
      }
      continue;
    }

    appendQueryValue(url, key, value);
  }
}

/**
 * Determines whether a query value should be serialized as repeated key-value pairs.
 *
 * @param value - The typed query value supplied by a caller.
 * @returns True when the value is a readonly array of primitive query values.
 */
function isQueryValueArray(value: ArchonApiQueryValue | readonly ArchonApiQueryValue[]): value is readonly ArchonApiQueryValue[] {
  // Array.isArray narrows to a mutable array by default, so this local predicate
  // preserves the readonly query contract while keeping serialization type-safe.
  return Array.isArray(value);
}

/**
 * Appends one query-string value when it is present.
 *
 * @param url - The URL object that receives the serialized query value.
 * @param key - The query-string key to append.
 * @param value - The primitive query value to serialize.
 */
function appendQueryValue(url: URL, key: string, value: ArchonApiQueryValue): void {
  // String conversion is centralized so booleans and numbers use stable browser
  // serialization while absent optional filters are skipped.
  if (value === undefined || value === null) {
    return;
  }

  url.searchParams.append(key, String(value));
}

/**
 * Creates request headers for JSON and empty-body requests.
 *
 * @param body - The optional request body that determines whether Content-Type is required.
 * @returns Headers suitable for a browser fetch call.
 */
function createRequestHeaders(body: unknown): Headers {
  // Every request accepts JSON responses, while Content-Type is only sent when a
  // JSON body exists so empty GET/DELETE requests remain minimal.
  const headers = new Headers({
    Accept: 'application/json',
  });

  if (body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }

  return headers;
}

/**
 * Parses a fetch response into typed data or a normalized HTTP error.
 *
 * @param response - The browser response returned by fetch.
 * @returns A request result containing parsed success data or a safe failure.
 */
async function parseResponse<TResponse>(response: Response): Promise<ArchonApiRequestResult<TResponse>> {
  // Empty responses are accepted for 204 and zero-length payloads. Non-empty
  // success responses must be JSON so feature code never consumes ambiguous text.
  const contentLength = response.headers.get('content-length');
  const hasNoBody = response.status === 204 || contentLength === '0';

  if (hasNoBody) {
    if (response.ok) {
      return { ok: true, data: undefined as TResponse, status: response.status };
    }

    return { ok: false, error: shapeHttpError({ status: response.status }), status: response.status };
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.toLowerCase().includes('application/json')) {
    return {
      ok: false,
      error: createNormalizedError({ category: 'unexpectedResponse', status: response.status, retryable: response.ok }),
      status: response.status,
    };
  }

  try {
    const body = await response.json() as unknown;
    if (response.ok) {
      return { ok: true, data: body as TResponse, status: response.status };
    }

    return { ok: false, error: shapeHttpError({ status: response.status, body }), status: response.status };
  } catch {
    return {
      ok: false,
      error: createNormalizedError({ category: 'unexpectedResponse', status: response.status, retryable: response.ok }),
      status: response.status,
    };
  }
}

/**
 * Connects a caller-provided AbortSignal to an internal AbortController.
 *
 * @param source - The optional caller signal supplied by a component or query.
 * @param target - The internal controller used by the executor for fetch and timeout aborts.
 * @returns A disposable listener handle used by the executor finally block.
 */
function createAbortForwarder(source: AbortSignal | undefined, target: AbortController): { dispose(): void } {
  // A separate controller allows the executor to merge caller cancellation and
  // timeout cancellation while preserving the specific abort reason for classification.
  if (source === undefined) {
    return { dispose: () => undefined };
  }

  const abort = (): void => target.abort(source.reason ?? new DOMException('The request was cancelled.', 'AbortError'));

  if (source.aborted) {
    abort();
    return { dispose: () => undefined };
  }

  source.addEventListener('abort', abort, { once: true });
  return {
    dispose: () => source.removeEventListener('abort', abort),
  };
}
