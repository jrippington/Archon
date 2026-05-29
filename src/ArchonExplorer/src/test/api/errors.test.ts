import { describe, expect, it } from 'vitest';
import {
  createNormalizedError,
  sanitizeDiagnosticMessage,
  shapeHttpError,
  shapeSafeQueryError,
  shapeThrownError,
  shapeValidationProblem,
} from '@/api/errors';

/**
 * Verifies the fail-closed diagnostic shaping used before errors reach UI state.
 */
describe('safe diagnostic shaping', () => {
  /**
   * Proves raw stack traces, connection strings, and database query fragments are
   * replaced with the caller-provided safe fallback message.
   */
  it('redacts unsafe diagnostic fragments', () => {
    expect(sanitizeDiagnosticMessage('System.InvalidOperationException at Service.Password=secret', 'safe fallback')).toBe('safe fallback');
    expect(sanitizeDiagnosticMessage('MATCH (n) RETURN n // Cypher', 'safe fallback')).toBe('safe fallback');
    expect(sanitizeDiagnosticMessage('A short safe message.', 'safe fallback')).toBe('A short safe message.');
  });

  /**
   * Confirms direct normalized errors never expose unsafe raw text even when a
   * backend or browser exception message is supplied as the candidate message.
   */
  it('creates normalized errors with safe fallback text', () => {
    const error = createNormalizedError({
      category: 'server',
      message: 'Neo4j driver failed with Password=secret',
      status: 500,
    });

    expect(error.category).toBe('server');
    expect(error.message).toBe('Archon API could not complete the request.');
    expect(error.retryable).toBe(true);
  });
});

/**
 * Verifies structured response envelopes are converted into safe frontend errors.
 */
describe('HTTP error shaping', () => {
  /**
   * Confirms ASP.NET Core validation-problem responses become field-level UI data
   * while unsafe validation messages are individually redacted.
   */
  it('shapes validation problem responses into safe validation issues', () => {
    const error = shapeValidationProblem({
      title: 'One or more validation errors occurred.',
      status: 400,
      traceId: 'trace-1',
      errors: {
        RepositoryRootDirectory: ['Repository root is required.'],
        Unsafe: ['System.Exception at Password=secret'],
      },
    });

    expect(error.category).toBe('validation');
    expect(error.status).toBe(400);
    expect(error.traceIdentifier).toBe('trace-1');
    expect(error.validationIssues).toEqual([
      { field: 'RepositoryRootDirectory', messages: ['Repository root is required.'] },
      { field: 'Unsafe', messages: ['The request could not be accepted because one or more values need attention.'] },
    ]);
    expect(error.retryable).toBe(false);
  });

  /**
   * Confirms the documented query error envelope preserves only safe code, safe
   * message, HTTP category, and support trace metadata.
   */
  it('shapes safe query error envelopes', () => {
    const error = shapeSafeQueryError({ code: 'ARCHON_QUERY_SCOPE_REQUIRED', message: 'Snapshot scope is required.', traceIdentifier: 'trace-2' }, 409);

    expect(error).toMatchObject({
      category: 'conflict',
      code: 'ARCHON_QUERY_SCOPE_REQUIRED',
      message: 'Snapshot scope is required.',
      status: 409,
      traceIdentifier: 'trace-2',
    });
  });

  /**
   * Confirms generic problem details are mapped by status and do not leak unsafe
   * detail text when the server accidentally includes implementation diagnostics.
   */
  it('maps generic problem details without leaking unsafe detail text', () => {
    const error = shapeHttpError({
      status: 500,
      body: {
        title: 'System.InvalidOperationException at GraphClient',
        detail: 'Password=secret',
        traceId: 'trace-3',
      },
    });

    expect(error.category).toBe('server');
    expect(error.message).toBe('Archon API could not complete the request.');
    expect(error.traceIdentifier).toBe('trace-3');
  });

  /**
   * Confirms status-code mapping covers controlled not-found and conflict responses
   * even when no structured problem body is available.
   */
  it('maps not-found and conflict responses by status code', () => {
    expect(shapeHttpError({ status: 404 }).category).toBe('notFound');
    expect(shapeHttpError({ status: 409 }).category).toBe('conflict');
  });
});

/**
 * Verifies thrown browser/runtime failures are classified without displaying raw
 * exception messages.
 */
describe('thrown error shaping', () => {
  /**
   * Confirms network failures use the safe API-unavailable category and message.
   */
  it('classifies network failures safely', () => {
    const error = shapeThrownError(new TypeError('Failed to fetch https://secret-host'));

    expect(error.category).toBe('network');
    expect(error.message).toBe('Archon API could not be reached. Check that the service is running and accessible.');
  });

  /**
   * Confirms timeout and cancellation abort reasons remain distinct for later UI
   * state and retry decisions.
   */
  it('classifies timeout and cancellation distinctly', () => {
    expect(shapeThrownError(new DOMException('timeout', 'TimeoutError')).category).toBe('timeout');
    expect(shapeThrownError(new DOMException('cancelled', 'AbortError')).category).toBe('cancelled');
  });
});
