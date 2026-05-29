import { describe, expect, it, vi } from 'vitest';
import { ArchonApiRequestExecutor, buildArchonApiUrl, type ArchonFetch } from '@/api/request';

/**
 * Creates a fetch test double that returns the supplied response and records the
 * request init object for assertions.
 *
 * @param response - The response that the fake fetch should resolve with.
 * @returns A Vitest mock compatible with the request executor fetch dependency.
 */
function createFetch(response: Response): ReturnType<typeof vi.fn<ArchonFetch>> {
  // The helper keeps individual test scenarios focused on request semantics rather
  // than repeating fetch mock boilerplate in every assertion.
  return vi.fn<ArchonFetch>().mockResolvedValue(response);
}

/**
 * Creates a JSON response with the content-type expected from ArchonApi.
 *
 * @param body - The body value serialized as JSON.
 * @param status - The HTTP status code for the response.
 * @returns A browser Response suitable for executor tests.
 */
function jsonResponse(body: unknown, status = 200): Response {
  // Response is available in the Vitest environment through the platform fetch API.
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

/**
 * Verifies URL construction and query serialization remain centralized in the
 * request layer.
 */
describe('buildArchonApiUrl', () => {
  /**
   * Confirms paths are joined to the configured base URL and optional query values
   * skip absent filters while repeating array values for ASP.NET Core binding.
   */
  it('builds absolute URLs with typed query parameters', () => {
    const url = buildArchonApiUrl('https://localhost:5001/root/', '/management/snapshots', {
      repositoryStableKey: 'repo://one',
      take: 25,
      includeWarnings: true,
      skipped: undefined,
      statuses: ['completed', 'failed'],
    });

    expect(url.toString()).toBe('https://localhost:5001/root/management/snapshots?repositoryStableKey=repo%3A%2F%2Fone&take=25&includeWarnings=true&statuses=completed&statuses=failed');
  });
});

/**
 * Verifies request execution for successful and intentionally empty responses.
 */
describe('ArchonApiRequestExecutor success handling', () => {
  /**
   * Confirms successful JSON responses are parsed, request bodies are serialized,
   * and JSON headers are applied consistently.
   */
  it('parses successful JSON responses and serializes JSON request bodies', async () => {
    const fetch = createFetch(jsonResponse({ status: 'Healthy' }));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' }),
      fetch,
    });

    const result = await executor.execute<{ status: string }, { requestedBy: string }>({
      method: 'POST',
      path: '/extractions',
      body: { requestedBy: 'tester' },
    });

    expect(result).toEqual({ ok: true, data: { status: 'Healthy' }, status: 200 });
    expect(fetch).toHaveBeenCalledTimes(1);
    const requestInit = fetch.mock.calls[0]?.[1];
    expect(requestInit?.method).toBe('POST');
    expect(requestInit?.body).toBe('{"requestedBy":"tester"}');
    expect((requestInit?.headers as Headers).get('content-type')).toBe('application/json');
  });

  /**
   * Confirms 204 responses produce an undefined data value without attempting JSON
   * parsing or reporting a malformed response.
   */
  it('handles empty success responses', async () => {
    const fetch = createFetch(new Response(null, { status: 204 }));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' }),
      fetch,
    });

    const result = await executor.execute<void>({ method: 'DELETE', path: '/management/snapshots/snapshot-1' });

    expect(result).toEqual({ ok: true, data: undefined, status: 204 });
  });
});

/**
 * Verifies missing configuration and failed HTTP responses are normalized safely.
 */
describe('ArchonApiRequestExecutor failure handling', () => {
  /**
   * Confirms absent base URL configuration short-circuits before fetch and returns
   * the safe configuration category.
   */
  it('returns a safe configuration error when the base URL is missing', async () => {
    const fetch = createFetch(jsonResponse({ status: 'Healthy' }));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: false }),
      fetch,
    });

    const result = await executor.execute({ method: 'GET', path: '/health' });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.category).toBe('configuration');
      expect(result.error.message).toBe('Archon API is not configured. Set the API base URL before retrying.');
    }
    expect(fetch).not.toHaveBeenCalled();
  });

  /**
   * Confirms validation-problem JSON is parsed and converted into safe normalized
   * validation issues.
   */
  it('normalizes validation problem responses', async () => {
    const fetch = createFetch(jsonResponse({
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { RepositoryRootDirectory: ['Repository root is required.'] },
    }, 400));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' }),
      fetch,
    });

    const result = await executor.execute({ method: 'POST', path: '/extractions', body: {} });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.category).toBe('validation');
      expect(result.error.validationIssues).toEqual([{ field: 'RepositoryRootDirectory', messages: ['Repository root is required.'] }]);
    }
  });

  /**
   * Confirms documented safe query error envelopes are normalized while preserving
   * safe code and support trace metadata.
   */
  it('normalizes safe query error responses', async () => {
    const fetch = createFetch(jsonResponse({ code: 'ARCHON_QUERY_SCOPE_REQUIRED', message: 'Snapshot scope is required.', traceIdentifier: 'trace-7' }, 409));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' }),
      fetch,
    });

    const result = await executor.execute({ method: 'GET', path: '/search' });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error).toMatchObject({ category: 'conflict', code: 'ARCHON_QUERY_SCOPE_REQUIRED', traceIdentifier: 'trace-7' });
    }
  });

  /**
   * Confirms raw unsafe problem-detail text is redacted before becoming UI state.
   */
  it('does not surface raw unsafe response text', async () => {
    const fetch = createFetch(jsonResponse({ title: 'System.Exception at Driver Password=secret' }, 500));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' }),
      fetch,
    });

    const result = await executor.execute({ method: 'GET', path: '/ready' });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.message).toBe('Archon API could not complete the request.');
    }
  });

  /**
   * Confirms network fetch failures are classified without exposing browser error
   * details or requested URLs.
   */
  it('classifies network failures safely', async () => {
    const fetch = vi.fn<ArchonFetch>().mockRejectedValue(new TypeError('Failed to fetch http://secret-host'));
    const executor = new ArchonApiRequestExecutor({
      getConfiguration: () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' }),
      fetch,
    });

    const result = await executor.execute({ method: 'GET', path: '/health' });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.category).toBe('network');
      expect(result.error.message).not.toContain('secret-host');
    }
  });

  /**
   * Confirms malformed JSON and unexpected content types are classified as
   * unexpected responses rather than leaking raw body text.
   */
  it('classifies malformed JSON and unexpected content types', async () => {
    const malformedFetch = createFetch(new Response('{not-json', { status: 200, headers: { 'content-type': 'application/json' } }));
    const textFetch = createFetch(new Response('System.Exception at Driver Password=secret', { status: 200, headers: { 'content-type': 'text/plain' } }));
    const configuration = () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' });

    const malformedResult = await new ArchonApiRequestExecutor({ getConfiguration: configuration, fetch: malformedFetch }).execute({ method: 'GET', path: '/health' });
    const textResult = await new ArchonApiRequestExecutor({ getConfiguration: configuration, fetch: textFetch }).execute({ method: 'GET', path: '/health' });

    expect(malformedResult.ok).toBe(false);
    expect(textResult.ok).toBe(false);
    if (!malformedResult.ok && !textResult.ok) {
      expect(malformedResult.error.category).toBe('unexpectedResponse');
      expect(textResult.error.category).toBe('unexpectedResponse');
      expect(textResult.error.message).not.toContain('Password');
    }
  });

  /**
   * Confirms caller cancellation and executor timeout produce distinct normalized
   * categories for later UI and retry behavior.
   */
  it('classifies cancellation and timeout aborts', async () => {
    const neverFetch = vi.fn<ArchonFetch>().mockImplementation((_input, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(init.signal?.reason), { once: true });
    }));
    const configuration = () => ({ isConfigured: true, baseUrl: 'https://localhost:5001' });

    const controller = new AbortController();
    const cancellationPromise = new ArchonApiRequestExecutor({ getConfiguration: configuration, fetch: neverFetch }).execute({ method: 'GET', path: '/health', signal: controller.signal });
    controller.abort(new DOMException('cancelled', 'AbortError'));
    const cancellationResult = await cancellationPromise;

    const timeoutResult = await new ArchonApiRequestExecutor({ getConfiguration: configuration, fetch: neverFetch }).execute({ method: 'GET', path: '/health', timeoutMs: 1 });

    expect(cancellationResult.ok).toBe(false);
    expect(timeoutResult.ok).toBe(false);
    if (!cancellationResult.ok && !timeoutResult.ok) {
      expect(cancellationResult.error.category).toBe('cancelled');
      expect(timeoutResult.error.category).toBe('timeout');
    }
  });
});
