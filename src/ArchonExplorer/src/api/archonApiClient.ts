import { archonApiRoutes } from './archonApiRoutes';
import type {
  DeleteAllSnapshotsRequest,
  DeleteAllSnapshotsResponse,
  DeleteSnapshotResponse,
  ExtractionRunHistoryResponse,
  ExtractionRunStatusResponse,
  ManagementHealthResponse,
  ManagementReadinessResponse,
  SnapshotLifecycleQuery,
  SnapshotLifecycleResponse,
  StartExtractionRequest,
} from './archonApiTypes';
import { archonApiRequestExecutor, type ArchonApiQuery, type ArchonApiRequestOptions, type ArchonApiRequestResult } from './request';

/**
 * Names retry behavior selected by a typed operational client method.
 */
export type ArchonApiClientRetryPolicy = 'default' | 'none';

/**
 * Describes common request controls accepted by operational client methods.
 */
export interface ArchonApiClientRequestControls {
  /**
   * Allows React Query, a component, or a caller-owned workflow to cancel the request.
   */
  readonly signal?: AbortSignal;

  /**
   * Applies timeout-compatible cancellation through the shared request executor.
   */
  readonly timeoutMs?: number;
}

/**
 * Extends the low-level request options with client-level retry intent.
 */
export type ArchonApiClientRequestOptions<TBody = unknown> = ArchonApiRequestOptions<TBody> & {
  /**
   * Records whether the typed client method is safe for future automatic retry helpers.
   */
  readonly retryPolicy?: ArchonApiClientRetryPolicy;
};

/**
 * Describes the executor dependency consumed by the typed operational client.
 */
export interface ArchonApiClientRequestExecutor {
  /**
   * Executes one request using the shared transport foundation.
   *
   * @param options - The route, method, body, query, cancellation, timeout, and retry intent selected by a client method.
   * @returns A typed success result or a normalized safe failure result.
   */
  execute<TResponse = void, TBody = unknown>(options: ArchonApiClientRequestOptions<TBody>): Promise<ArchonApiRequestResult<TResponse>>;
}

/**
 * Describes dependencies used to construct an operational ArchonApi client.
 */
export interface ArchonApiClientOptions {
  /**
   * Supplies the request executor used by production code or test doubles.
   */
  readonly execute?: ArchonApiClientRequestExecutor['execute'];
}

/**
 * Describes optional filters for extraction run history.
 */
export interface ExtractionRunHistoryQuery {
  /**
   * Limits the number of recent extraction run summaries returned by the API.
   */
  readonly take?: number;
}

/**
 * Describes optional filters for management run-history queries.
 */
export interface ManagementRunHistoryQuery {
  /**
   * Limits the number of recent management run summaries returned by the API.
   */
  readonly take?: number;
}

/**
 * Provides typed operational methods over the shared ArchonApi request foundation.
 */
export class ArchonApiClient {
  /**
   * Executes route-catalog-backed HTTP requests for every operational method.
   */
  private readonly executeRequest: ArchonApiClientRequestExecutor['execute'];

  /**
   * Initializes a client with production request execution or a test-supplied delegate.
   *
   * @param options - Optional dependency overrides for deterministic tests or specialized hosts.
   */
  public constructor(options: ArchonApiClientOptions = {}) {
    // The client owns typed method shape only; the request executor still owns base URL,
    // fetch, JSON parsing, cancellation, timeout, and safe error normalization.
    this.executeRequest = options.execute ?? archonApiRequestExecutor.execute.bind(archonApiRequestExecutor);
  }

  /**
   * Reads the ArchonApi health endpoint.
   *
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed health response or a normalized safe failure.
   */
  public getHealth(controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<ManagementHealthResponse>> {
    // Health is a safe idempotent read used by connectivity checks and setup indicators.
    return this.executeRequest<ManagementHealthResponse>({
      method: 'GET',
      path: archonApiRoutes.operations.health,
      ...controls,
    });
  }

  /**
   * Reads the ArchonApi readiness endpoint.
   *
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed readiness response or a normalized safe failure.
   */
  public getReadiness(controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<ManagementReadinessResponse>> {
    // Readiness proves configured dependencies can support work; callers must keep the
    // dependency detail safe and avoid exposing raw backend diagnostics.
    return this.executeRequest<ManagementReadinessResponse>({
      method: 'GET',
      path: archonApiRoutes.operations.ready,
      ...controls,
    });
  }

  /**
   * Starts an extraction run through the operational extraction route.
   *
   * @param request - The typed extraction request accepted by `POST /extractions`.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The accepted run status response or a normalized safe failure.
   */
  public startExtraction(request: StartExtractionRequest, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<ExtractionRunStatusResponse>> {
    // Starting extraction is not a feature screen here; this wrapper only centralizes
    // the route, method, and typed body for later operational UI packages.
    return this.executeRequest<ExtractionRunStatusResponse, StartExtractionRequest>({
      method: 'POST',
      path: archonApiRoutes.extraction.start,
      body: request,
      ...controls,
    });
  }

  /**
   * Reads the current status for one accepted extraction run.
   *
   * @param runId - The public extraction run identifier returned by the API.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed run status response or a normalized safe failure.
   */
  public getExtractionStatus(runId: string, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<ExtractionRunStatusResponse>> {
    // The route catalog performs path encoding so unusual run IDs cannot break the path.
    return this.executeRequest<ExtractionRunStatusResponse>({
      method: 'GET',
      path: archonApiRoutes.extraction.byRunId(runId),
      ...controls,
    });
  }

  /**
   * Reads recent extraction run history.
   *
   * @param query - Optional bounded history filters accepted by the API.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed extraction history response or a normalized safe failure.
   */
  public getExtractionHistory(query: ExtractionRunHistoryQuery = {}, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<ExtractionRunHistoryResponse>> {
    // Query serialization remains delegated to the request executor so client methods
    // do not hand-build query strings.
    return this.executeRequest<ExtractionRunHistoryResponse>({
      method: 'GET',
      path: archonApiRoutes.extraction.runs,
      query: toArchonApiQuery({ ...query }),
      ...controls,
    });
  }

  /**
   * Lists snapshot lifecycle rows through the management API.
   *
   * @param query - Optional lifecycle filters such as repository, solution, status, time bounds, commit, or take.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed snapshot lifecycle response or a normalized safe failure.
   */
  public listSnapshots(query: SnapshotLifecycleQuery = {}, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<SnapshotLifecycleResponse>> {
    // Snapshot lifecycle reads are safe queries; destructive lifecycle changes use
    // separate methods with explicit no-retry intent.
    return this.executeRequest<SnapshotLifecycleResponse>({
      method: 'GET',
      path: archonApiRoutes.management.snapshots,
      query: toArchonApiQuery({ ...query }),
      ...controls,
    });
  }

  /**
   * Deletes one snapshot by stable key.
   *
   * @param snapshotStableKey - The public snapshot stable key selected by a caller that already confirmed deletion intent.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed delete-one response or a normalized safe failure.
   */
  public deleteSnapshot(snapshotStableKey: string, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<DeleteSnapshotResponse>> {
    // Deletion is deliberately marked as no-retry so future mutation helpers cannot
    // accidentally replay destructive operations through generic retry defaults.
    return this.executeRequest<DeleteSnapshotResponse>({
      method: 'DELETE',
      path: archonApiRoutes.management.snapshotByStableKey(snapshotStableKey),
      retryPolicy: 'none',
      ...controls,
    });
  }

  /**
   * Deletes all snapshots through the explicit confirmation contract.
   *
   * @param request - The delete-all request carrying the exact confirmation phrase and optional requester metadata.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The typed delete-all response or a normalized safe failure.
   */
  public deleteAllSnapshots(request: DeleteAllSnapshotsRequest, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<DeleteAllSnapshotsResponse>> {
    // The method accepts the API's confirmation request object instead of hiding the
    // confirmation phrase so feature screens remain responsible for explicit user intent.
    return this.executeRequest<DeleteAllSnapshotsResponse, DeleteAllSnapshotsRequest>({
      method: 'POST',
      path: archonApiRoutes.management.deleteAllSnapshots,
      body: request,
      retryPolicy: 'none',
      ...controls,
    });
  }

  /**
   * Reads management run history through `GET /management/runs`.
   *
   * @param query - Optional bounded history filters accepted by the management endpoint.
   * @param controls - Optional cancellation and timeout controls supplied by the caller.
   * @returns The extraction-history-shaped management run response or a normalized safe failure.
   */
  public getManagementRuns(query: ManagementRunHistoryQuery = {}, controls: ArchonApiClientRequestControls = {}): Promise<ArchonApiRequestResult<ExtractionRunHistoryResponse>> {
    // The current management route exposes operational run-history semantics; the typed
    // response can be refined later if the backend diverges from extraction history shape.
    return this.executeRequest<ExtractionRunHistoryResponse>({
      method: 'GET',
      path: archonApiRoutes.management.runs,
      query: toArchonApiQuery({ ...query }),
      ...controls,
    });
  }
}

/**
 * Shared production operational client used by hooks and future feature slices.
 */
export const archonApiClient = new ArchonApiClient();

/**
 * Converts a typed query object into the request executor's generic query shape.
 *
 * @param query - A readonly typed query object with primitive property values.
 * @returns The same data in the request executor's query type.
 */
function toArchonApiQuery<TQuery extends object>(query: TQuery): ArchonApiQuery {
  // The executor performs final omission and serialization of undefined/null values;
  // this helper only bridges structurally typed DTOs into the generic query contract.
  return query as ArchonApiQuery;
}
