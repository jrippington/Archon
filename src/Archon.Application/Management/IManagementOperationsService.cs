namespace Archon.Application.Management
{
    /// <summary>
    /// Defines safe management and operational use cases exposed by the management API module.
    /// </summary>
    public interface IManagementOperationsService
    {
        /// <summary>
        /// Registers repository metadata without starting extraction or allowing arbitrary mutation.
        /// </summary>
        /// <param name="request">The repository registration request to validate and store.</param>
        /// <param name="cancellationToken">The cancellation token for the registration operation.</param>
        /// <returns>The accepted repository registration or validation errors.</returns>
        Task<ManagementOperationResult<RepositoryRegistrationResponse>> RegisterRepositoryAsync(RegisterRepositoryRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Registers a solution path under an existing repository context.
        /// </summary>
        /// <param name="request">The solution registration request to validate and store.</param>
        /// <param name="cancellationToken">The cancellation token for the registration operation.</param>
        /// <returns>The accepted solution registration or validation errors.</returns>
        Task<ManagementOperationResult<SolutionRegistrationResponse>> RegisterSolutionAsync(RegisterSolutionRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Applies an approved metadata overlay to a controlled management target.
        /// </summary>
        /// <param name="request">The metadata update request to validate and apply.</param>
        /// <param name="cancellationToken">The cancellation token for the metadata operation.</param>
        /// <returns>The accepted metadata overlay or validation errors.</returns>
        Task<ManagementOperationResult<MetadataUpdateResponse>> UpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Lists snapshot lifecycle rows using controlled filters and result bounds.
        /// </summary>
        /// <param name="query">The lifecycle query filters and bounds.</param>
        /// <param name="cancellationToken">The cancellation token for the lifecycle query.</param>
        /// <returns>The bounded snapshot lifecycle response or validation errors.</returns>
        Task<ManagementOperationResult<SnapshotLifecycleResponse>> ListSnapshotsAsync(SnapshotLifecycleQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes one persisted snapshot and its snapshot-scoped records by public stable key.
        /// </summary>
        /// <param name="request">The delete-one request containing the target snapshot stable key and audit actor.</param>
        /// <param name="cancellationToken">The cancellation token for the destructive operation.</param>
        /// <returns>The safe deletion response or validation errors.</returns>
        Task<ManagementOperationResult<DeleteSnapshotResponse>> DeleteSnapshotAsync(DeleteSnapshotRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes every persisted snapshot and every snapshot-scoped record after explicit confirmation.
        /// </summary>
        /// <param name="request">The delete-all request containing the required confirmation phrase and audit actor.</param>
        /// <param name="cancellationToken">The cancellation token for the destructive operation.</param>
        /// <returns>The safe aggregate deletion response or validation errors.</returns>
        Task<ManagementOperationResult<DeleteAllSnapshotsResponse>> DeleteAllSnapshotsAsync(DeleteAllSnapshotsRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Validates and optionally executes snapshot retention inside the requested lifecycle scope.
        /// </summary>
        /// <param name="request">The retention request to validate and execute.</param>
        /// <param name="cancellationToken">The cancellation token for the retention operation.</param>
        /// <returns>The retention outcome or validation errors.</returns>
        Task<ManagementOperationResult<RetentionResponse>> ApplyRetentionAsync(RetentionRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Lists extraction run history with safe metadata and diagnostic counts.
        /// </summary>
        /// <param name="query">The run-history query filters and bounds.</param>
        /// <param name="cancellationToken">The cancellation token for the run-history query.</param>
        /// <returns>The bounded run-history response or validation errors.</returns>
        Task<ManagementOperationResult<ExtractionRunHistoryResponse>> ListExtractionRunsAsync(ExtractionRunHistoryQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Changes the controlled enablement state for one rule code and version.
        /// </summary>
        /// <param name="request">The rule enablement request to validate and store.</param>
        /// <param name="cancellationToken">The cancellation token for the rule enablement operation.</param>
        /// <returns>The accepted rule enablement state or validation errors.</returns>
        Task<ManagementOperationResult<RuleEnablementResponse>> SetRuleEnablementAsync(RuleEnablementRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Executes or previews one supported maintenance operation without exposing arbitrary mutation.
        /// </summary>
        /// <param name="request">The maintenance request to validate and execute.</param>
        /// <param name="cancellationToken">The cancellation token for the maintenance operation.</param>
        /// <returns>The maintenance outcome or validation errors.</returns>
        Task<ManagementOperationResult<MaintenanceResponse>> RunMaintenanceAsync(MaintenanceRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Gets local management health without sensitive dependency details.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for the health check.</param>
        /// <returns>The safe health response.</returns>
        Task<ManagementHealthResponse> GetHealthAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets sanitized readiness for dependencies required by controlled query and management operations.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for the readiness check.</param>
        /// <returns>The safe readiness response.</returns>
        Task<ManagementReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken);
    }
}
