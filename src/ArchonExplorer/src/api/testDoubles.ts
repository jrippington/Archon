import { archonApiRoutes, type ArchonApiPath } from './archonApiRoutes';
import type {
  DeleteAllSnapshotsRequest,
  DeleteAllSnapshotsResponse,
  DeleteSnapshotResponse,
  ExtractionRunHistoryResponse,
  ExtractionRunStatusResponse,
  ManagementHealthResponse,
  ManagementReadinessResponse,
  SnapshotLifecycleItemResponse,
  SnapshotLifecycleQuery,
  SnapshotLifecycleResponse,
  StartExtractionRequest,
} from './archonApiTypes';
import type { ArchonApiRequestResult } from './request';

/**
 * Describes one request recorded by the ArchonApi client test double.
 */
export interface ArchonApiTestDoubleRequest {
  /**
   * Contains the typed client method name that was invoked.
   */
  readonly operation: string;

  /**
   * Contains the route-catalog path used by the operation.
   */
  readonly path: ArchonApiPath;

  /**
   * Contains the HTTP method represented by the operation.
   */
  readonly method: 'GET' | 'POST' | 'DELETE';

  /**
   * Contains the safe request body supplied to a typed operation when present.
   */
  readonly body?: unknown;

  /**
   * Contains the typed query object supplied to a typed operation when present.
   */
  readonly query?: unknown;
}

/**
 * Describes mutable seed data accepted by the ArchonApi runtime test double.
 */
export interface ArchonApiTestDoubleOptions {
  /**
   * Supplies the health response returned by `getHealth`.
   */
  readonly health?: ManagementHealthResponse;

  /**
   * Supplies the readiness response returned by `getReadiness`.
   */
  readonly readiness?: ManagementReadinessResponse;

  /**
   * Supplies extraction run statuses indexed by public run identifier.
   */
  readonly extractionRuns?: Readonly<Record<string, ExtractionRunStatusResponse>>;

  /**
   * Supplies ordered status responses returned each time a run is polled.
   */
  readonly extractionRunSequences?: Readonly<Record<string, readonly ExtractionRunStatusResponse[]>>;

  /**
   * Supplies snapshot lifecycle rows returned by `listSnapshots`.
   */
  readonly snapshots?: readonly SnapshotLifecycleItemResponse[];
}

/**
 * Provides a deterministic typed ArchonApi client test double for component and journey tests.
 */
export class ArchonApiClientTestDouble {
  /**
   * Stores every typed operation call in invocation order for test assertions.
   */
  private readonly recordedRequests: ArchonApiTestDoubleRequest[] = [];

  /**
   * Stores mutable extraction run responses indexed by run identifier.
   */
  private readonly extractionRuns = new Map<string, ExtractionRunStatusResponse>();

  /**
   * Stores ordered poll responses for runs that should change state during a test journey.
   */
  private readonly extractionRunSequences = new Map<string, ExtractionRunStatusResponse[]>();

  /**
   * Stores mutable snapshot lifecycle rows used by list and deletion operations.
   */
  private snapshots: SnapshotLifecycleItemResponse[];

  /**
   * Stores the health response returned by the operational health method.
   */
  private readonly health: ManagementHealthResponse;

  /**
   * Stores the readiness response returned by the operational readiness method.
   */
  private readonly readiness: ManagementReadinessResponse;

  /**
   * Initializes the test double with deterministic default or caller-supplied seed data.
   *
   * @param options - Optional health, readiness, extraction, and snapshot seed data.
   */
  public constructor(options: ArchonApiTestDoubleOptions = {}) {
    // Defaults keep component tests runnable without a live backend while preserving
    // the same safe contract shapes returned by the production typed client.
    this.health = options.health ?? createHealthResponse();
    this.readiness = options.readiness ?? createReadinessResponse();
    this.snapshots = [...(options.snapshots ?? [createSnapshotLifecycleItem()])];

    const runs = options.extractionRuns ?? { [defaultRunId]: createExtractionRunStatus({ runId: defaultRunId }) };
    for (const [runId, status] of Object.entries(runs)) {
      this.extractionRuns.set(runId, status);
    }

    for (const [runId, statuses] of Object.entries(options.extractionRunSequences ?? {})) {
      // Each sequence is copied because polling mutates the remaining response queue while tests
      // should still be free to reuse their original fixtures for assertions.
      this.extractionRunSequences.set(runId, [...statuses]);
      if (statuses[0] !== undefined && !this.extractionRuns.has(runId)) {
        this.extractionRuns.set(runId, statuses[0]);
      }
    }
  }

  /**
   * Gets the recorded typed operation calls.
   *
   * @returns A readonly snapshot of requests captured by the test double.
   */
  public get requests(): readonly ArchonApiTestDoubleRequest[] {
    // A copy is returned so tests cannot mutate the double's internal call history.
    return [...this.recordedRequests];
  }

  /**
   * Reads the deterministic health response.
   *
   * @returns A successful health request result.
   */
  public async getHealth(): Promise<ArchonApiRequestResult<ManagementHealthResponse>> {
    // The recorded path comes from the route catalog to prove test consumers do not
    // need duplicate route strings when asserting runtime behavior.
    this.record({ operation: 'getHealth', method: 'GET', path: archonApiRoutes.operations.health });
    return ok(this.health);
  }

  /**
   * Reads the deterministic readiness response.
   *
   * @returns A successful readiness request result.
   */
  public async getReadiness(): Promise<ArchonApiRequestResult<ManagementReadinessResponse>> {
    // Readiness remains separate from health so connectivity tests can prove the
    // same two-step operational flow used by the production runtime.
    this.record({ operation: 'getReadiness', method: 'GET', path: archonApiRoutes.operations.ready });
    return ok(this.readiness);
  }

  /**
   * Starts a deterministic extraction run and stores the accepted status.
   *
   * @param request - The typed extraction request supplied by a test or component.
   * @returns A successful extraction run status result.
   */
  public async startExtraction(request: StartExtractionRequest): Promise<ArchonApiRequestResult<ExtractionRunStatusResponse>> {
    // The generated run stays deterministic so journey tests can start a run and then
    // poll it without depending on clocks, random values, or a live ArchonApi instance.
    const runId = request.requestedBy === undefined || request.requestedBy === null ? defaultRunId : `run-${request.requestedBy}`;
    const status = createExtractionRunStatus({ runId, repositoryRootDirectory: request.repositoryRootDirectory ?? 'D:/repo' });
    this.extractionRuns.set(runId, status);
    this.record({ operation: 'startExtraction', method: 'POST', path: archonApiRoutes.extraction.start, body: request });
    return ok(status, 202);
  }

  /**
   * Reads one deterministic extraction run status.
   *
   * @param runId - The public run identifier to resolve from the seeded run map.
   * @returns A successful status result, or a safe not-found result when the run is absent.
   */
  public async getExtractionStatus(runId: string): Promise<ArchonApiRequestResult<ExtractionRunStatusResponse>> {
    // Missing runs return the same normalized result shape as the request foundation,
    // enabling component tests to exercise unavailable polling without raw diagnostics.
    this.record({ operation: 'getExtractionStatus', method: 'GET', path: archonApiRoutes.extraction.byRunId(runId) });
    const sequence = this.extractionRunSequences.get(runId);
    if (sequence !== undefined && sequence.length > 0) {
      // Sequence-backed runs let component and browser-like tests prove queued/running/completed
      // transitions without timers, random values, or a live ArchonApi scheduler.
      const nextStatus = sequence.shift();
      if (nextStatus !== undefined) {
        this.extractionRuns.set(runId, nextStatus);
        return ok(nextStatus);
      }
    }

    const status = this.extractionRuns.get(runId);
    if (status === undefined) {
      return failure('notFound', 'Extraction run was not found.', false, 404);
    }

    return ok(status);
  }

  /**
   * Reads deterministic extraction run history.
   *
   * @param query - Optional result bound matching the typed operational client shape.
   * @returns A successful extraction history result ordered by insertion order.
   */
  public async getExtractionHistory(query: { readonly take?: number } = {}): Promise<ArchonApiRequestResult<ExtractionRunHistoryResponse>> {
    // History uses the status map so tests that start runs through the double see the
    // same runs in later history calls.
    this.record({ operation: 'getExtractionHistory', method: 'GET', path: archonApiRoutes.extraction.runs, query });
    const runs = [...this.extractionRuns.values()].slice(0, query.take).map((run) => ({
      runId: run.runId,
      status: run.status,
      startedUtc: run.startedUtc,
      completedUtc: run.completedUtc,
      repositoryRootDirectory: run.submittedRequest.repositoryRootDirectory,
      solutionCount: run.submittedRequest.solutionPaths.length,
      warningCount: run.warningCount,
      errorCount: run.errorCount,
      snapshotIdentity: run.snapshotIdentity,
    }));

    return ok({ runs });
  }

  /**
   * Lists deterministic snapshot lifecycle rows.
   *
   * @param query - Optional lifecycle filters accepted by the typed operational client.
   * @returns A successful snapshot lifecycle response.
   */
  public async listSnapshots(query: SnapshotLifecycleQuery = {}): Promise<ArchonApiRequestResult<SnapshotLifecycleResponse>> {
    // The double applies only simple deterministic filters needed by component tests;
    // backend-specific query semantics remain owned by ArchonApi.
    this.record({ operation: 'listSnapshots', method: 'GET', path: archonApiRoutes.management.snapshots, query });
    const filtered = this.snapshots.filter((snapshot) => matchesSnapshotQuery(snapshot, query));
    const items = filtered.slice(0, query.take ?? filtered.length);
    return ok({ items, totalCount: filtered.length, take: query.take ?? filtered.length, warnings: [] });
  }

  /**
   * Deletes one deterministic snapshot row by stable key.
   *
   * @param snapshotStableKey - The public snapshot stable key selected by a test or component.
   * @returns A successful delete-one response with deterministic counts.
   */
  public async deleteSnapshot(snapshotStableKey: string): Promise<ArchonApiRequestResult<DeleteSnapshotResponse>> {
    // Deletion is recorded but never retried automatically; future mutation helpers can
    // assert this operation explicitly without invoking transport retry behavior.
    this.record({ operation: 'deleteSnapshot', method: 'DELETE', path: archonApiRoutes.management.snapshotByStableKey(snapshotStableKey) });
    const beforeCount = this.snapshots.length;
    this.snapshots = this.snapshots.filter((snapshot) => snapshot.snapshotStableKey !== snapshotStableKey);
    const deleted = this.snapshots.length !== beforeCount;
    return ok(createDeleteSnapshotResponse(snapshotStableKey, deleted));
  }

  /**
   * Deletes every deterministic snapshot row after explicit confirmation.
   *
   * @param request - The typed delete-all request carrying the confirmation phrase.
   * @returns A successful delete-all response when confirmed, or a safe validation failure otherwise.
   */
  public async deleteAllSnapshots(request: DeleteAllSnapshotsRequest): Promise<ArchonApiRequestResult<DeleteAllSnapshotsResponse>> {
    // The confirmation phrase is enforced in the test double so component tests cannot
    // accidentally bypass the destructive-operation contract used by production code.
    this.record({ operation: 'deleteAllSnapshots', method: 'POST', path: archonApiRoutes.management.deleteAllSnapshots, body: request });
    if (request.confirmation !== 'delete-all-snapshots') {
      return failure('validation', 'The delete-all confirmation phrase is required.', false, 400);
    }

    const deletedSnapshotCount = this.snapshots.length;
    this.snapshots = [];
    return ok(createDeleteAllSnapshotsResponse(deletedSnapshotCount));
  }

  /**
   * Records one typed operation call.
   *
   * @param request - The operation, method, path, and optional body/query values to record.
   */
  private record(request: ArchonApiTestDoubleRequest): void {
    // Recording is intentionally append-only so tests can assert complete journey order.
    this.recordedRequests.push(request);
  }
}

/**
 * Public default run identifier used by deterministic test-double seed data.
 */
export const defaultRunId = 'run-001';

/**
 * Creates a successful request result for test-double methods.
 *
 * @param data - The typed response payload to return.
 * @param status - The HTTP status code represented by the fake response.
 * @returns A successful request-result envelope.
 */
export function ok<TResponse>(data: TResponse, status = 200): ArchonApiRequestResult<TResponse> {
  // The helper mirrors the production request result shape without requiring fetch.
  return { ok: true, data, status };
}

/**
 * Creates a safe failed request result for test-double methods.
 *
 * @param category - The normalized failure category to expose to UI code.
 * @param message - The safe user-facing message selected by the fake operation.
 * @param retryable - Indicates whether retry is safe for this fake failure.
 * @param status - Optional HTTP status code represented by the fake failure.
 * @returns A failed request-result envelope with safe diagnostics only.
 */
export function failure<TResponse>(category: ArchonApiRequestResult<TResponse> extends never ? never : 'configuration' | 'network' | 'timeout' | 'validation' | 'notFound' | 'conflict' | 'server' | 'unexpectedResponse' | 'cancelled' | 'unknown', message: string, retryable: boolean, status?: number): ArchonApiRequestResult<TResponse> {
  // The test double never returns raw backend text, stack traces, URLs, or secrets.
  return { ok: false, status, error: { category, message, retryable, status } };
}

/**
 * Creates a deterministic health response.
 *
 * @returns A healthy management response suitable for connectivity tests.
 */
export function createHealthResponse(): ManagementHealthResponse {
  // The timestamp is fixed so snapshots and assertions remain stable across runs.
  return { status: 'Healthy', checkedUtc: '2026-01-01T00:00:00Z', checks: ['self'], warnings: [] };
}

/**
 * Creates a deterministic readiness response.
 *
 * @returns A ready management response suitable for connectivity tests.
 */
export function createReadinessResponse(): ManagementReadinessResponse {
  // Dependency names are safe and generic; no connection details are included.
  return { status: 'Ready', checkedUtc: '2026-01-01T00:00:00Z', dependencies: [{ name: 'graph', status: 'Ready', message: 'Ready.' }], warnings: [] };
}

/**
 * Creates a deterministic extraction run status response.
 *
 * @param overrides - Optional run identity, status, and repository values for a scenario.
 * @returns A typed extraction run status response.
 */
export function createExtractionRunStatus(overrides: { readonly runId?: string; readonly status?: string; readonly repositoryRootDirectory?: string } = {}): ExtractionRunStatusResponse {
  // The response includes every required contract member so component tests can use it
  // as realistic server state without a live extraction pipeline.
  const status = overrides.status ?? 'Running';
  const completedUtc = status.toLowerCase() === 'completed' ? '2026-01-01T00:05:00Z' : null;
  return {
    runId: overrides.runId ?? defaultRunId,
    status,
    submittedRequest: {
      repositoryRootDirectory: overrides.repositoryRootDirectory ?? 'D:/repo',
      solutionPaths: ['Archon.sln'],
      branchName: 'main',
      commitSha: 'abc123',
      requestedBy: 'test-double',
      metadataKeys: [],
    },
    startedUtc: '2026-01-01T00:00:00Z',
    completedUtc,
    progress: { stage: 'Extraction', message: 'Extraction is running.', percentage: completedUtc === null ? 50 : 100, lastUpdatedUtc: '2026-01-01T00:02:00Z' },
    warningCount: 0,
    errorCount: status.toLowerCase() === 'failed' ? 1 : 0,
    timings: [],
    snapshotIdentity: completedUtc === null ? null : 'snapshot://repo/current',
    persistenceDiagnostics: null,
  };
}

/**
 * Creates a deterministic snapshot lifecycle row.
 *
 * @param overrides - Optional identity and scope values for a scenario.
 * @returns A typed snapshot lifecycle row.
 */
export function createSnapshotLifecycleItem(overrides: Partial<SnapshotLifecycleItemResponse> = {}): SnapshotLifecycleItemResponse {
  // The stable keys look URI-like because production stable keys may contain slash-like
  // separators that route builders must encode and query keys must preserve as identity.
  return {
    snapshotStableKey: 'snapshot://repo/current',
    repositoryStableKey: 'repository://repo',
    solutionStableKey: 'solution://repo/Archon.sln',
    status: 'Completed',
    branchName: 'main',
    commitSha: 'abc123',
    startedUtc: '2026-01-01T00:00:00Z',
    completedUtc: '2026-01-01T00:05:00Z',
    warningCount: 0,
    errorCount: 0,
    ...overrides,
  };
}

/**
 * Creates a deterministic delete-one snapshot response.
 *
 * @param snapshotStableKey - The snapshot identity targeted by deletion.
 * @param deleted - Indicates whether a matching seed row was removed.
 * @returns A typed delete-one response.
 */
function createDeleteSnapshotResponse(snapshotStableKey: string, deleted: boolean): DeleteSnapshotResponse {
  // Counts are deterministic and intentionally small because tests usually care about
  // mutation semantics and safe shape rather than storage-engine delete volume.
  return {
    snapshotStableKey,
    deleted,
    deletedSnapshotCount: deleted ? 1 : 0,
    deletedNodeCount: deleted ? 10 : 0,
    deletedRelationshipCount: deleted ? 20 : 0,
    affectedRunCount: deleted ? 1 : 0,
    warnings: [],
    audit: createAuditMetadata(),
  };
}

/**
 * Creates a deterministic delete-all snapshot response.
 *
 * @param deletedSnapshotCount - The number of seeded snapshot rows removed.
 * @returns A typed delete-all response.
 */
function createDeleteAllSnapshotsResponse(deletedSnapshotCount: number): DeleteAllSnapshotsResponse {
  // Aggregate counts scale from the row count so assertions remain predictable.
  return {
    deletedSnapshotCount,
    deletedNodeCount: deletedSnapshotCount * 10,
    deletedRelationshipCount: deletedSnapshotCount * 20,
    affectedRunCount: deletedSnapshotCount,
    warnings: [],
    audit: createAuditMetadata(),
  };
}

/**
 * Creates deterministic audit metadata for destructive-operation fake responses.
 *
 * @returns Typed audit metadata with safe fixed values.
 */
function createAuditMetadata(): DeleteSnapshotResponse['audit'] {
  // Audit metadata stays safe and fixed so tests can compare it without leaking user data.
  return { requestedBy: 'test-double', requestedUtc: '2026-01-01T00:10:00Z', correlationId: 'correlation-test-double' };
}

/**
 * Determines whether a snapshot row matches the simple filters implemented by the test double.
 *
 * @param snapshot - The snapshot lifecycle row being evaluated.
 * @param query - The optional lifecycle query supplied by a test or component.
 * @returns True when the row should appear in the fake list response.
 */
function matchesSnapshotQuery(snapshot: SnapshotLifecycleItemResponse, query: SnapshotLifecycleQuery): boolean {
  // Only stable-key, status, and commit filters are applied because date-range behavior
  // belongs to the backend; this is enough to keep component tests deterministic.
  if (query.repositoryStableKey !== undefined && snapshot.repositoryStableKey !== query.repositoryStableKey) {
    return false;
  }

  if (query.solutionStableKey !== undefined && snapshot.solutionStableKey !== query.solutionStableKey) {
    return false;
  }

  if (query.status !== undefined && snapshot.status !== query.status) {
    return false;
  }

  if (query.commitSha !== undefined && snapshot.commitSha !== query.commitSha) {
    return false;
  }

  return true;
}