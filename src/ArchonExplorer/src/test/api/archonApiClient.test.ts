import { describe, expect, it } from 'vitest';
import { ArchonApiClient, type ArchonApiClientRequestOptions } from '@/api/archonApiClient';
import type { ArchonApiRequestOptions, ArchonApiRequestResult } from '@/api/request';
import type {
  DeleteAllSnapshotsRequest,
  DeleteAllSnapshotsResponse,
  DeleteSnapshotResponse,
  ExtractionRunHistoryResponse,
  ExtractionRunStatusResponse,
  ManagementHealthResponse,
  ManagementReadinessResponse,
  SnapshotLifecycleResponse,
  StartExtractionRequest,
} from '@/api/archonApiTypes';

/**
 * Creates a successful typed request result for client tests.
 *
 * @param data - The response payload returned by the fake request executor.
 * @returns A normalized successful request result with an HTTP 200 status.
 */
function ok<TResponse>(data: TResponse): ArchonApiRequestResult<TResponse> {
  // The client tests assert delegation semantics, so a tiny success helper keeps
  // response payload construction separate from route and method expectations.
  return { ok: true, data, status: 200 };
}

/**
 * Describes the recorded request options captured by the test executor.
 */
type RecordedClientRequest = ArchonApiClientRequestOptions<unknown>;

/**
 * Describes the deterministic executor test double used by client tests.
 */
interface RecordingExecutor {
  /**
   * Executes a generic client request and records it for later assertions.
   */
  readonly execute: <TResponse = void, TBody = unknown>(options: ArchonApiClientRequestOptions<TBody>) => Promise<ArchonApiRequestResult<TResponse>>;

  /**
   * Contains request options in call order.
   */
  readonly requests: RecordedClientRequest[];

  /**
   * Adds the next response returned by the executor.
   *
   * @param result - The normalized request result to return for the next client call.
   */
  enqueue(result: ArchonApiRequestResult<unknown>): void;
}

/**
 * Creates a request executor test double that records every delegated request.
 *
 * @returns A Vitest mock matching the client executor contract.
 */
function createExecutor(): RecordingExecutor {
  // The fake keeps generic typing local to the execute method and records raw options
  // separately, avoiding Vitest mock covariance issues under strict TypeScript.
  const requests: RecordedClientRequest[] = [];
  const responses: ArchonApiRequestResult<unknown>[] = [];

  return {
    requests,
    enqueue: (result) => responses.push(result),
    execute: async <TResponse = void, TBody = unknown>(options: ArchonApiClientRequestOptions<TBody>): Promise<ArchonApiRequestResult<TResponse>> => {
      requests.push(options as RecordedClientRequest);
      return (responses.shift() ?? ok(undefined)) as ArchonApiRequestResult<TResponse>;
    },
  };
}

/**
 * Verifies that the operational client delegates every method to the shared
 * request executor and route catalog instead of duplicating HTTP strings.
 */
describe('ArchonApiClient operational methods', () => {
  /**
   * Confirms health and readiness checks use the documented operational routes.
   */
  it('calls health and readiness routes with GET requests', async () => {
    const execute = createExecutor();
    execute.enqueue(ok<ManagementHealthResponse>({ status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: [], warnings: [] }));
    execute.enqueue(ok<ManagementReadinessResponse>({ status: 'Ready', checkedUtc: '2026-01-01T00:00:00Z', dependencies: [], warnings: [] }));
    const client = new ArchonApiClient({ execute: execute.execute });

    await client.getHealth();
    await client.getReadiness();

    expect(execute.requests[0]).toMatchObject({ method: 'GET', path: '/health' });
    expect(execute.requests[1]).toMatchObject({ method: 'GET', path: '/ready' });
  });

  /**
   * Confirms extraction start, status, and history methods use the existing
   * `/extractions` route family and preserve caller-supplied options.
   */
  it('calls extraction routes with typed request bodies and query values', async () => {
    const execute = createExecutor();
    const startRequest: StartExtractionRequest = { repositoryRootDirectory: 'D:/repo', requestedBy: 'tester' };
    execute.enqueue(ok<ExtractionRunStatusResponse>({} as ExtractionRunStatusResponse));
    execute.enqueue(ok<ExtractionRunStatusResponse>({} as ExtractionRunStatusResponse));
    execute.enqueue(ok<ExtractionRunHistoryResponse>({ runs: [] }));
    const client = new ArchonApiClient({ execute: execute.execute });

    await client.startExtraction(startRequest, { timeoutMs: 5000 });
    await client.getExtractionStatus('run/with space');
    await client.getExtractionHistory({ take: 10 });

    expect(execute.requests[0]).toMatchObject({ method: 'POST', path: '/extractions', body: startRequest, timeoutMs: 5000 });
    expect(execute.requests[1]).toMatchObject({ method: 'GET', path: '/extractions/run%2Fwith%20space' });
    expect(execute.requests[2]).toMatchObject({ method: 'GET', path: '/extractions', query: { take: 10 } });
  });

  /**
   * Confirms snapshot list and run-history support use management routes without
   * copying route strings into feature code.
   */
  it('calls snapshot lifecycle and management run-history routes', async () => {
    const execute = createExecutor();
    execute.enqueue(ok<SnapshotLifecycleResponse>({ items: [], totalCount: 0, take: 25, warnings: [] }));
    execute.enqueue(ok<ExtractionRunHistoryResponse>({ runs: [] }));
    const client = new ArchonApiClient({ execute: execute.execute });

    await client.listSnapshots({ repositoryStableKey: 'repository://one', take: 25 });
    await client.getManagementRuns({ take: 5 });

    expect(execute.requests[0]).toMatchObject({ method: 'GET', path: '/management/snapshots', query: { repositoryStableKey: 'repository://one', take: 25 } });
    expect(execute.requests[1]).toMatchObject({ method: 'GET', path: '/management/runs', query: { take: 5 } });
  });

  /**
   * Confirms destructive snapshot operations require explicit caller input and
   * carry a no-automatic-retry marker for later mutation helpers.
   */
  it('calls destructive snapshot routes without automatic retry eligibility', async () => {
    const execute = createExecutor();
    const deleteAllRequest: DeleteAllSnapshotsRequest = { confirmation: 'delete-all-snapshots', requestedBy: 'tester' };
    execute.enqueue(ok<DeleteSnapshotResponse>({} as DeleteSnapshotResponse));
    execute.enqueue(ok<DeleteAllSnapshotsResponse>({} as DeleteAllSnapshotsResponse));
    const client = new ArchonApiClient({ execute: execute.execute });

    await client.deleteSnapshot('snapshot://repo/solution/current');
    await client.deleteAllSnapshots(deleteAllRequest);

    expect(execute.requests[0]).toMatchObject({ method: 'DELETE', path: '/management/snapshots/snapshot%3A%2F%2Frepo%2Fsolution%2Fcurrent', retryPolicy: 'none' });
    expect(execute.requests[1]).toMatchObject({ method: 'POST', path: '/management/snapshots/delete-all', body: deleteAllRequest, retryPolicy: 'none' });
  });

  /**
   * Confirms methods forward AbortSignal values so TanStack Query and callers can
   * cancel operational requests without a bespoke transport path.
   */
  it('forwards cancellation signals to delegated requests', async () => {
    const execute = createExecutor();
    const controller = new AbortController();
    execute.enqueue(ok<ExtractionRunHistoryResponse>({ runs: [] }));
    const client = new ArchonApiClient({ execute: execute.execute });

    await client.getExtractionHistory({ take: 3 }, { signal: controller.signal });

    const options = execute.requests[0] as ArchonApiRequestOptions | undefined;
    expect(options?.signal).toBe(controller.signal);
  });
});
