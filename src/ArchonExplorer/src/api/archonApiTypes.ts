/**
 * Names every normalized frontend error category produced by the ArchonExplorer
 * request foundation.
 */
export type ArchonApiErrorCategory =
  | 'configuration'
  | 'network'
  | 'timeout'
  | 'validation'
  | 'notFound'
  | 'conflict'
  | 'server'
  | 'unexpectedResponse'
  | 'cancelled'
  | 'unknown';

/**
 * Describes a field-level or form-level validation issue that is safe for UI
 * presentation.
 */
export interface NormalizedValidationIssue {
  /**
   * Identifies the field or stable validation-code bucket that produced the issue.
   */
  readonly field: string;

  /**
   * Contains safe validation messages associated with the field or validation-code bucket.
   */
  readonly messages: readonly string[];
}

/**
 * Represents the safe error object consumed by feature components, notifications,
 * status indicators, and tests.
 */
export interface NormalizedArchonApiError {
  /**
   * Classifies the failure without exposing transport, framework, or database internals.
   */
  readonly category: ArchonApiErrorCategory;

  /**
   * Provides the safe user-visible summary for the failure.
   */
  readonly message: string;

  /**
   * Carries the HTTP status code when the failure came from an HTTP response.
   */
  readonly status?: number;

  /**
   * Names the stable machine-readable backend code when a safe code was supplied.
   */
  readonly code?: string;

  /**
   * Preserves a safe server correlation or trace identifier when one is available.
   */
  readonly traceIdentifier?: string;

  /**
   * Contains validation issues for validation failures and remains absent for other categories.
   */
  readonly validationIssues?: readonly NormalizedValidationIssue[];

  /**
   * Indicates whether a future UI should offer retry for this class of failure.
   */
  readonly retryable: boolean;
}

/**
 * Models the ASP.NET Core problem-details shape used by controlled server
 * failures.
 */
export interface ProblemDetailsResponse {
  /**
   * Identifies the problem-details type URI when the backend provides one.
   */
  readonly type?: string;

  /**
   * Provides the safe problem title when supplied by ASP.NET Core.
   */
  readonly title?: string;

  /**
   * Carries the HTTP status code represented by the problem response.
   */
  readonly status?: number;

  /**
   * Describes the failure only when the server has already shaped the text safely.
   */
  readonly detail?: string;

  /**
   * Names the request instance associated with the problem response.
   */
  readonly instance?: string;

  /**
   * Carries an optional support correlation identifier emitted by ASP.NET Core.
   */
  readonly traceId?: string;

  /**
   * Carries an optional support correlation identifier used by some custom envelopes.
   */
  readonly traceIdentifier?: string;
}

/**
 * Models the validation-problem response shape emitted by ASP.NET Core and the
 * Archon management/extraction validation factories.
 */
export interface ValidationProblemDetailsResponse extends ProblemDetailsResponse {
  /**
   * Maps field names or stable validation codes to one or more safe validation messages.
   */
  readonly errors?: Record<string, readonly string[]>;
}

/**
 * Models the safe query error envelope returned by documented query endpoints.
 */
export interface SafeQueryErrorResponse {
  /**
   * Contains a stable machine-readable code that can be shown or logged safely.
   */
  readonly code: string;

  /**
   * Contains the backend-approved safe message for the documented query failure.
   */
  readonly message: string;

  /**
   * Carries an optional trace identifier for support correlation.
   */
  readonly traceIdentifier?: string | null;
}

/**
 * Represents the configured snapshot selector used by later query and operational
 * consumers.
 */
export type SnapshotSelector =
  | 'current'
  | {
      /**
       * Selects an explicit persisted snapshot by stable public identity.
       */
      readonly snapshotStableKey: string;
    };

/**
 * Represents the JSON request body accepted by POST /extractions.
 */
export interface StartExtractionRequest {
  /**
   * Supplies the repository root directory to inspect, or lets the API validate absence.
   */
  readonly repositoryRootDirectory?: string | null;

  /**
   * Supplies explicit solution paths to include in the extraction request.
   */
  readonly solutionPaths?: readonly string[] | null;

  /**
   * Carries optional source-control branch metadata for the submitted run.
   */
  readonly branchName?: string | null;

  /**
   * Carries optional source-control commit metadata for the submitted run.
   */
  readonly commitSha?: string | null;

  /**
   * Identifies the actor or subsystem that requested extraction when known.
   */
  readonly requestedBy?: string | null;

  /**
   * Carries deterministic metadata values for the API while status responses expose keys only.
   */
  readonly metadata?: Record<string, string> | null;
}

/**
 * Represents the accepted extraction request summary returned by run-status
 * responses.
 */
export interface ExtractionRunRequestSummaryResponse {
  /**
   * Contains the normalized repository root directory accepted for extraction.
   */
  readonly repositoryRootDirectory: string;

  /**
   * Contains normalized solution paths accepted for the extraction run.
   */
  readonly solutionPaths: readonly string[];

  /**
   * Contains optional branch metadata supplied by the caller.
   */
  readonly branchName: string | null;

  /**
   * Contains optional commit metadata supplied by the caller.
   */
  readonly commitSha: string | null;

  /**
   * Contains optional actor metadata supplied by the caller.
   */
  readonly requestedBy: string | null;

  /**
   * Contains submitted metadata keys without exposing metadata values.
   */
  readonly metadataKeys: readonly string[];
}

/**
 * Represents the safe current progress section for an extraction run.
 */
export interface ExtractionRunProgressResponse {
  /**
   * Names the current lifecycle or workflow stage.
   */
  readonly stage: string;

  /**
   * Contains the safe human-readable progress message.
   */
  readonly message: string;

  /**
   * Carries optional progress percentage when the backend can measure it.
   */
  readonly percentage: number | null;

  /**
   * Contains the UTC timestamp for the latest progress update.
   */
  readonly lastUpdatedUtc: string;
}

/**
 * Represents one extraction stage timing measurement.
 */
export interface ExtractionRunTimingResponse {
  /**
   * Names the measured stage or pipeline step.
   */
  readonly stage: string;

  /**
   * Carries elapsed time in milliseconds for the measured step.
   */
  readonly elapsedMilliseconds: number;

  /**
   * Contains the UTC timestamp when the measured step completed.
   */
  readonly completedUtc: string;
}

/**
 * Represents persistence-specific counts attached to an extraction run.
 */
export interface ExtractionRunPersistenceCountsResponse {
  /** Number of repository records included in persistence. */
  readonly repositoryCount: number;
  /** Number of solution records included in persistence. */
  readonly solutionCount: number;
  /** Number of project records included in persistence. */
  readonly projectCount: number;
  /** Number of file or document records included in persistence. */
  readonly fileCount: number;
  /** Number of generalized architecture nodes included in persistence. */
  readonly nodeCount: number;
  /** Number of generalized architecture relationships included in persistence. */
  readonly relationshipCount: number;
  /** Number of evidence records included in persistence. */
  readonly evidenceCount: number;
  /** Number of finding records included in persistence. */
  readonly findingCount: number;
  /** Number of persistence warnings recorded for the run. */
  readonly warningCount: number;
  /** Number of persistence errors recorded for the run. */
  readonly errorCount: number;
  /** Number of metric records included in persistence. */
  readonly metricCount: number;
  /** Number of generated summary records included in persistence. */
  readonly generatedSummaryCount: number;
  /** Optional number of metadata entries when the backend can measure them. */
  readonly metadataEntryCount: number | null;
  /** Optional number of persistence operations when the backend can measure them. */
  readonly persistenceOperationCount: number | null;
  /** Optional number of persistence batches when the backend can measure them. */
  readonly persistenceBatchCount: number | null;
  /** Optional serialized payload size when materialization produced a measurable payload. */
  readonly serializedPayloadBytes: number | null;
}

/**
 * Represents the persistence diagnostic section returned by extraction status.
 */
export interface ExtractionRunPersistenceDiagnosticsResponse {
  /**
   * Contains ordered persistence sub-stage timing measurements.
   */
  readonly timings: readonly ExtractionRunTimingResponse[];

  /**
   * Contains persistence volume and operation counts for the run.
   */
  readonly counts: ExtractionRunPersistenceCountsResponse;

  /**
   * Indicates whether the persistence diagnostic set represents a completed attempt.
   */
  readonly completed: boolean;
}

/**
 * Represents the current API-visible state of an extraction run.
 */
export interface ExtractionRunStatusResponse {
  /** Stable public run identifier. */
  readonly runId: string;
  /** Current lifecycle status name. */
  readonly status: string;
  /** Accepted request summary. */
  readonly submittedRequest: ExtractionRunRequestSummaryResponse;
  /** UTC timestamp when the run was accepted. */
  readonly startedUtc: string;
  /** Optional UTC timestamp when the run reached a terminal state. */
  readonly completedUtc: string | null;
  /** Current progress details. */
  readonly progress: ExtractionRunProgressResponse;
  /** Number of warning diagnostics recorded so far. */
  readonly warningCount: number;
  /** Number of error diagnostics recorded so far. */
  readonly errorCount: number;
  /** Measured extraction stage durations recorded so far. */
  readonly timings: readonly ExtractionRunTimingResponse[];
  /** Optional persisted snapshot stable identity. */
  readonly snapshotIdentity: string | null;
  /** Optional persistence-specific diagnostic breakdown for the run. */
  readonly persistenceDiagnostics: ExtractionRunPersistenceDiagnosticsResponse | null;
}

/**
 * Represents the compact run summary returned by GET /extractions.
 */
export interface ExtractionRunSummaryResponse {
  /** Stable public run identifier. */
  readonly runId: string;
  /** Current lifecycle status name. */
  readonly status: string;
  /** UTC timestamp when the run was accepted. */
  readonly startedUtc: string;
  /** Optional UTC timestamp when the run reached a terminal state. */
  readonly completedUtc: string | null;
  /** Normalized repository root directory retained in the submitted request summary. */
  readonly repositoryRootDirectory: string;
  /** Number of submitted solutions accepted for the run. */
  readonly solutionCount: number;
  /** Number of warning diagnostics currently recorded for the run. */
  readonly warningCount: number;
  /** Number of error diagnostics currently recorded for the run. */
  readonly errorCount: number;
  /** Optional persisted snapshot stable identity when persistence has completed. */
  readonly snapshotIdentity: string | null;
}

/**
 * Represents the recent extraction run history response from GET /extractions.
 */
export interface ExtractionRunHistoryResponse {
  /**
   * Contains recent run summaries in deterministic newest-first order.
   */
  readonly runs: readonly ExtractionRunSummaryResponse[];
}

/**
 * Represents safe local health status for the management module.
 */
export interface ManagementHealthResponse {
  /** Aggregate health status for the management module. */
  readonly status: string;
  /** UTC timestamp when health was evaluated. */
  readonly checkedUtc: string;
  /** Safe health checks that contributed to the aggregate status. */
  readonly checks: readonly string[];
  /** Safe warnings explaining degraded but locally usable conditions. */
  readonly warnings: readonly string[];
}

/**
 * Represents one sanitized dependency readiness check.
 */
export interface DependencyReadinessResponse {
  /** Public dependency name, such as graph persistence or rule catalog. */
  readonly name: string;
  /** Dependency readiness status without sensitive connection details. */
  readonly status: string;
  /** Safe explanation for the dependency status. */
  readonly message: string;
}

/**
 * Represents readiness of required query dependencies.
 */
export interface ManagementReadinessResponse {
  /** Aggregate readiness status. */
  readonly status: string;
  /** UTC timestamp when readiness was evaluated. */
  readonly checkedUtc: string;
  /** Sanitized dependency readiness rows. */
  readonly dependencies: readonly DependencyReadinessResponse[];
  /** Safe warnings explaining degraded readiness. */
  readonly warnings: readonly string[];
}

/**
 * Represents one snapshot lifecycle row safe for operational callers.
 */
export interface SnapshotLifecycleItemResponse {
  /** Stable snapshot identity. */
  readonly snapshotStableKey: string;
  /** Stable repository identity associated with the snapshot. */
  readonly repositoryStableKey: string;
  /** Optional stable solution identity inferred for the snapshot. */
  readonly solutionStableKey: string | null;
  /** Safe lifecycle status text. */
  readonly status: string;
  /** Optional branch name recorded for the snapshot. */
  readonly branchName: string | null;
  /** Optional source-control commit SHA recorded for the snapshot. */
  readonly commitSha: string | null;
  /** UTC timestamp when extraction started. */
  readonly startedUtc: string;
  /** Optional UTC timestamp when extraction completed. */
  readonly completedUtc: string | null;
  /** Number of snapshot warnings without expanding message content. */
  readonly warningCount: number;
  /** Number of snapshot errors without expanding message content. */
  readonly errorCount: number;
}

/**
 * Represents a bounded snapshot lifecycle query result.
 */
export interface SnapshotLifecycleResponse {
  /** Lifecycle rows returned after filtering and bounds were applied. */
  readonly items: readonly SnapshotLifecycleItemResponse[];
  /** Total rows matching filters before the take bound. */
  readonly totalCount: number;
  /** Effective result-size bound. */
  readonly take: number;
  /** Safe warnings explaining unavailable or truncated lifecycle data. */
  readonly warnings: readonly string[];
}

/**
 * Represents query parameters accepted by GET /management/snapshots.
 */
export interface SnapshotLifecycleQuery {
  /** Optional repository stable-key filter. */
  readonly repositoryStableKey?: string;
  /** Optional solution stable-key filter. */
  readonly solutionStableKey?: string;
  /** Optional lifecycle status filter. */
  readonly status?: string;
  /** Optional inclusive start timestamp filter. */
  readonly fromUtc?: string;
  /** Optional inclusive end timestamp filter. */
  readonly toUtc?: string;
  /** Optional commit SHA filter. */
  readonly commitSha?: string;
  /** Optional result-size bound. */
  readonly take?: number;
}

/**
 * Represents audit-ready metadata attached to accepted management actions.
 */
export interface AuditMetadataResponse {
  /** Normalized actor identity associated with the management action. */
  readonly requestedBy: string;
  /** UTC timestamp when the application accepted the action. */
  readonly requestedUtc: string;
  /** Generated correlation identity for tracing one management action. */
  readonly correlationId: string;
}

/**
 * Represents the safe response returned after deleting one persisted snapshot.
 */
export interface DeleteSnapshotResponse {
  /** Public stable key targeted by the deletion operation. */
  readonly snapshotStableKey: string;
  /** Indicates whether a matching snapshot was deleted. */
  readonly deleted: boolean;
  /** Number of snapshot header records deleted. */
  readonly deletedSnapshotCount: number;
  /** Number of snapshot-scoped data nodes deleted. */
  readonly deletedNodeCount: number;
  /** Number of relationships deleted where practical for the backing store. */
  readonly deletedRelationshipCount: number;
  /** Number of preserved extraction run records that referenced the deleted snapshot. */
  readonly affectedRunCount: number;
  /** Credential-safe warnings about deletion completeness or count precision. */
  readonly warnings: readonly string[];
  /** Audit metadata created when the destructive operation was accepted. */
  readonly audit: AuditMetadataResponse;
}

/**
 * Represents a management request to delete every persisted snapshot after
 * explicit destructive-operation confirmation.
 */
export interface DeleteAllSnapshotsRequest {
  /** Confirmation phrase that must equal delete-all-snapshots before cleanup is accepted. */
  readonly confirmation: string;
  /** Optional actor identity recorded in audit metadata for the destructive operation. */
  readonly requestedBy?: string | null;
}

/**
 * Represents the safe response returned after deleting every persisted snapshot.
 */
export interface DeleteAllSnapshotsResponse {
  /** Number of snapshot header records deleted. */
  readonly deletedSnapshotCount: number;
  /** Number of snapshot-scoped data nodes deleted. */
  readonly deletedNodeCount: number;
  /** Number of relationships deleted where practical for the backing store. */
  readonly deletedRelationshipCount: number;
  /** Number of preserved extraction run records that referenced deleted snapshots. */
  readonly affectedRunCount: number;
  /** Credential-safe warnings about deletion completeness, count precision, or preserved run-history semantics. */
  readonly warnings: readonly string[];
  /** Audit metadata created when the destructive operation was accepted. */
  readonly audit: AuditMetadataResponse;
}
