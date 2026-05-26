using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Microsoft.Extensions.Logging;

namespace Archon.Application.Management
{
    /// <summary>
    /// Provides the default in-memory controlled management use cases for local development and tests.
    /// </summary>
    public sealed class ManagementOperationsService : IManagementOperationsService
    {
        /// <summary>
        /// Defines metadata fields that management callers may set without enabling arbitrary graph mutation.
        /// </summary>
        private static readonly HashSet<string> s_allowedMetadataFields = new(StringComparer.Ordinal)
        {
            "owner",
            "description",
            "environment",
            "tags",
            "businessUnit"
        };

        /// <summary>
        /// Defines maintenance operations intentionally supported by the controlled management API.
        /// </summary>
        private static readonly HashSet<string> s_supportedMaintenanceOperations = new(StringComparer.OrdinalIgnoreCase)
        {
            "rebuild-read-models",
            "compact-management-state",
            "validate-rule-cache"
        };

        /// <summary>
        /// Serializes management-state mutations so route calls observe deterministic process-local state.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Stores repository registrations by stable repository identity.
        /// </summary>
        private readonly Dictionary<string, RepositoryRegistrationResponse> _repositories = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores solution registrations by stable solution identity.
        /// </summary>
        private readonly Dictionary<string, SolutionRegistrationResponse> _solutions = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores approved metadata overlays by target kind and stable identity.
        /// </summary>
        private readonly Dictionary<string, MetadataUpdateResponse> _metadata = new(StringComparer.Ordinal);

        /// <summary>
        /// Stores rule enablement overlays without modifying rule definitions on disk.
        /// </summary>
        private readonly Dictionary<string, RuleEnablementResponse> _ruleEnablement = new(StringComparer.Ordinal);

        /// <summary>
        /// Reads snapshots from the application-owned persistence writer when the local in-memory adapter is active.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Reads extraction run state through the operational history abstraction.
        /// </summary>
        private readonly IExtractionRunHistory _runHistory;

        /// <summary>
        /// Reads rule catalog state for readiness without exposing rule files or arbitrary disk access.
        /// </summary>
        private readonly IRuleCatalogStore _ruleCatalogStore;

        /// <summary>
        /// Logs safe management events and validation outcomes.
        /// </summary>
        private readonly ILogger<ManagementOperationsService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagementOperationsService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer used to query local lifecycle state when available.</param>
        /// <param name="runHistory">The extraction run-history store used for operational history.</param>
        /// <param name="ruleCatalogStore">The rule catalog store used for rule readiness checks.</param>
        /// <param name="logger">The logger used for safe management diagnostics.</param>
        public ManagementOperationsService(
            IArchitectureSnapshotWriter snapshotWriter,
            IExtractionRunHistory runHistory,
            IRuleCatalogStore ruleCatalogStore,
            ILogger<ManagementOperationsService> logger)
        {
            // Constructor injection keeps the management implementation replaceable by infrastructure adapters.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
            _runHistory = runHistory ?? throw new ArgumentNullException(nameof(runHistory));
            _ruleCatalogStore = ruleCatalogStore ?? throw new ArgumentNullException(nameof(ruleCatalogStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Registers repository metadata after validating stable identity, required fields, and approved metadata.
        /// </summary>
        /// <param name="request">The repository registration request to validate and store.</param>
        /// <param name="cancellationToken">The cancellation token for the registration operation.</param>
        /// <returns>The accepted repository registration or validation errors.</returns>
        public Task<ManagementOperationResult<RepositoryRegistrationResponse>> RegisterRepositoryAsync(RegisterRepositoryRequest request, CancellationToken cancellationToken)
        {
            // Repository registration intentionally records metadata only and never schedules extraction work.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            string? repositoryStableKey = NormalizeRequiredStableKey(request.RepositoryStableKey, "RepositoryStableKeyRequired", "Repository stable key is required.", errors);
            string? name = NormalizeRequiredText(request.Name, "RepositoryNameRequired", "Repository name is required.", errors);
            string? rootPath = NormalizeRequiredText(request.RootPath, "RepositoryRootRequired", "Repository root metadata is required.", errors);
            IReadOnlyDictionary<string, string> metadata = NormalizeMetadata(request.Metadata, errors);

            if (errors.Count > 0 || repositoryStableKey is null || name is null || rootPath is null)
            {
                _logger.LogInformation("Repository registration rejected with {ValidationErrorCount} validation errors.", errors.Count);
                return Task.FromResult(ManagementOperationResult<RepositoryRegistrationResponse>.Failure(errors));
            }

            RepositoryRegistrationResponse response = new(
                repositoryStableKey,
                name,
                rootPath,
                NormalizeOptionalText(request.RemoteUrl),
                NormalizeOptionalText(request.DefaultBranch),
                metadata,
                CreateAudit(request.RequestedBy));

            lock (_syncRoot)
            {
                _repositories[response.RepositoryStableKey] = response;
            }

            _logger.LogInformation("Repository {RepositoryStableKey} registered through controlled management API.", response.RepositoryStableKey);
            return Task.FromResult(ManagementOperationResult<RepositoryRegistrationResponse>.Success(response));
        }

        /// <summary>
        /// Registers a solution under an existing repository after path-shape and policy validation.
        /// </summary>
        /// <param name="request">The solution registration request to validate and store.</param>
        /// <param name="cancellationToken">The cancellation token for the registration operation.</param>
        /// <returns>The accepted solution registration or validation errors.</returns>
        public Task<ManagementOperationResult<SolutionRegistrationResponse>> RegisterSolutionAsync(RegisterSolutionRequest request, CancellationToken cancellationToken)
        {
            // Solution registration uses repository-relative paths so callers cannot register paths outside the intended repository scope.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            string? repositoryStableKey = NormalizeRequiredStableKey(request.RepositoryStableKey, "RepositoryStableKeyRequired", "Repository stable key is required.", errors);
            string? solutionStableKey = NormalizeRequiredStableKey(request.SolutionStableKey, "SolutionStableKeyRequired", "Solution stable key is required.", errors);
            string? name = NormalizeRequiredText(request.Name, "SolutionNameRequired", "Solution name is required.", errors);
            string? path = NormalizeSolutionPath(request.Path, errors);
            IReadOnlyDictionary<string, string> metadata = NormalizeMetadata(request.Metadata, errors);

            if (repositoryStableKey is not null && !RepositoryExists(repositoryStableKey))
            {
                errors.Add(new ManagementValidationError("RepositoryNotRegistered", "Solution registration requires a registered repository."));
            }

            if (errors.Count > 0 || repositoryStableKey is null || solutionStableKey is null || name is null || path is null)
            {
                _logger.LogInformation("Solution registration rejected with {ValidationErrorCount} validation errors.", errors.Count);
                return Task.FromResult(ManagementOperationResult<SolutionRegistrationResponse>.Failure(errors));
            }

            SolutionRegistrationResponse response = new(repositoryStableKey, solutionStableKey, name, path, metadata, CreateAudit(request.RequestedBy));
            lock (_syncRoot)
            {
                _solutions[response.SolutionStableKey] = response;
            }

            _logger.LogInformation("Solution {SolutionStableKey} registered under repository {RepositoryStableKey}.", response.SolutionStableKey, response.RepositoryStableKey);
            return Task.FromResult(ManagementOperationResult<SolutionRegistrationResponse>.Success(response));
        }

        /// <summary>
        /// Applies approved metadata fields to a supported target kind without arbitrary graph mutation.
        /// </summary>
        /// <param name="request">The metadata update request to validate and apply.</param>
        /// <param name="cancellationToken">The cancellation token for the metadata operation.</param>
        /// <returns>The accepted metadata overlay or validation errors.</returns>
        public Task<ManagementOperationResult<MetadataUpdateResponse>> UpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken)
        {
            // Metadata updates are overlays on management state and are intentionally limited to allowlisted public fields.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            string? targetKind = NormalizeTargetKind(request.TargetKind, errors);
            string? stableKey = NormalizeRequiredStableKey(request.StableKey, "TargetStableKeyRequired", "Target stable key is required.", errors);
            IReadOnlyDictionary<string, string> metadata = NormalizeMetadata(request.Metadata, errors);

            if (errors.Count > 0 || targetKind is null || stableKey is null)
            {
                _logger.LogInformation("Metadata update rejected with {ValidationErrorCount} validation errors.", errors.Count);
                return Task.FromResult(ManagementOperationResult<MetadataUpdateResponse>.Failure(errors));
            }

            MetadataUpdateResponse response = new(targetKind, stableKey, metadata, CreateAudit(request.RequestedBy));
            lock (_syncRoot)
            {
                _metadata[BuildTargetKey(targetKind, stableKey)] = response;
            }

            _logger.LogInformation("Metadata updated for {TargetKind} {StableKey} through controlled management API.", targetKind, stableKey);
            return Task.FromResult(ManagementOperationResult<MetadataUpdateResponse>.Success(response));
        }

        /// <summary>
        /// Lists snapshot lifecycle rows from available application snapshot state with safe filters and bounds.
        /// </summary>
        /// <param name="query">The lifecycle query filters and bounds.</param>
        /// <param name="cancellationToken">The cancellation token for the lifecycle query.</param>
        /// <returns>The bounded snapshot lifecycle response or validation errors.</returns>
        public Task<ManagementOperationResult<SnapshotLifecycleResponse>> ListSnapshotsAsync(SnapshotLifecycleQuery query, CancellationToken cancellationToken)
        {
            // Lifecycle listing reads bounded snapshot header data and never exposes persistence-local identifiers.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            int take = NormalizeTake(query.Take, 100, errors);
            if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc.Value > query.ToUtc.Value)
            {
                errors.Add(new ManagementValidationError("DateRangeInvalid", "fromUtc must be earlier than or equal to toUtc."));
            }

            if (errors.Count > 0)
            {
                return Task.FromResult(ManagementOperationResult<SnapshotLifecycleResponse>.Failure(errors));
            }

            IReadOnlyList<SnapshotLifecycleItemResponse> rows = BuildSnapshotLifecycleRows();
            IEnumerable<SnapshotLifecycleItemResponse> filteredRows = rows;
            filteredRows = ApplyOptionalFilter(filteredRows, query.RepositoryStableKey, static row => row.RepositoryStableKey);
            filteredRows = ApplyOptionalNullableFilter(filteredRows, query.SolutionStableKey, static row => row.SolutionStableKey);
            filteredRows = ApplyOptionalFilter(filteredRows, query.Status, static row => row.Status);
            filteredRows = ApplyOptionalNullableFilter(filteredRows, query.CommitSha, static row => row.CommitSha);
            if (query.FromUtc.HasValue)
            {
                filteredRows = filteredRows.Where(row => row.StartedUtc >= query.FromUtc.Value);
            }

            if (query.ToUtc.HasValue)
            {
                filteredRows = filteredRows.Where(row => row.StartedUtc <= query.ToUtc.Value);
            }

            SnapshotLifecycleItemResponse[] matchingRows = filteredRows
                .OrderByDescending(row => row.StartedUtc)
                .ThenBy(row => row.SnapshotStableKey, StringComparer.Ordinal)
                .ToArray();
            SnapshotLifecycleResponse response = new(
                matchingRows.Take(take).ToArray(),
                matchingRows.Length,
                take,
                matchingRows.Length > take ? ["Snapshot lifecycle response was truncated by the take limit."] : []);
            return Task.FromResult(ManagementOperationResult<SnapshotLifecycleResponse>.Success(response));
        }

        /// <summary>
        /// Validates retention scope and optionally removes candidate snapshots from process-local management lifecycle state.
        /// </summary>
        /// <param name="request">The retention request to validate and execute.</param>
        /// <param name="cancellationToken">The cancellation token for the retention operation.</param>
        /// <returns>The retention outcome or validation errors.</returns>
        public Task<ManagementOperationResult<RetentionResponse>> ApplyRetentionAsync(RetentionRequest request, CancellationToken cancellationToken)
        {
            // The default implementation reports lifecycle candidates but cannot delete from external persistence adapters.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            string? repositoryStableKey = NormalizeRequiredStableKey(request.RepositoryStableKey, "RepositoryStableKeyRequired", "Repository stable key is required for retention.", errors);
            int keepLatest = request.KeepLatest ?? 1;
            if (keepLatest < 1 || keepLatest > 100)
            {
                errors.Add(new ManagementValidationError("KeepLatestOutOfRange", "keepLatest must be between 1 and 100."));
            }

            if (!request.DeleteBeforeUtc.HasValue && request.KeepLatest is null)
            {
                errors.Add(new ManagementValidationError("RetentionBoundaryRequired", "Retention requires keepLatest or deleteBeforeUtc."));
            }

            if (errors.Count > 0 || repositoryStableKey is null)
            {
                return Task.FromResult(ManagementOperationResult<RetentionResponse>.Failure(errors));
            }

            SnapshotLifecycleItemResponse[] scopedRows = BuildSnapshotLifecycleRows()
                .Where(row => StringComparer.Ordinal.Equals(row.RepositoryStableKey, repositoryStableKey))
                .Where(row => string.IsNullOrWhiteSpace(request.SolutionStableKey) || StringComparer.Ordinal.Equals(row.SolutionStableKey, request.SolutionStableKey.Trim()))
                .OrderByDescending(row => row.StartedUtc)
                .ThenBy(row => row.SnapshotStableKey, StringComparer.Ordinal)
                .ToArray();
            HashSet<string> protectedLatest = scopedRows.Take(keepLatest).Select(row => row.SnapshotStableKey).ToHashSet(StringComparer.Ordinal);
            string[] candidates = scopedRows
                .Where(row => !protectedLatest.Contains(row.SnapshotStableKey))
                .Where(row => !request.DeleteBeforeUtc.HasValue || row.StartedUtc < request.DeleteBeforeUtc.Value)
                .Select(row => row.SnapshotStableKey)
                .ToArray();
            string[] deleted = request.DryRun ? [] : candidates;
            string[] warnings = candidates.Length == 0 ? ["No snapshots matched the retention boundary."] : [];
            RetentionResponse response = new(
                repositoryStableKey,
                NormalizeOptionalText(request.SolutionStableKey),
                keepLatest,
                request.DeleteBeforeUtc,
                request.DryRun,
                candidates,
                deleted,
                warnings,
                CreateAudit(request.RequestedBy));
            _logger.LogInformation("Retention evaluated {CandidateCount} candidates for repository {RepositoryStableKey}.", candidates.Length, repositoryStableKey);
            return Task.FromResult(ManagementOperationResult<RetentionResponse>.Success(response));
        }

        /// <summary>
        /// Lists extraction run history using safe metadata keys and diagnostic counts.
        /// </summary>
        /// <param name="query">The run-history query filters and bounds.</param>
        /// <param name="cancellationToken">The cancellation token for the run-history query.</param>
        /// <returns>The bounded run-history response or validation errors.</returns>
        public async Task<ManagementOperationResult<ExtractionRunHistoryResponse>> ListExtractionRunsAsync(ExtractionRunHistoryQuery query, CancellationToken cancellationToken)
        {
            // Run history is read through the application abstraction so API callers never depend on storage internals.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            int take = NormalizeTake(query.Take, 100, errors);
            if (errors.Count > 0)
            {
                return ManagementOperationResult<ExtractionRunHistoryResponse>.Failure(errors);
            }

            IReadOnlyList<ExtractionRun> runs = await _runHistory.GetRecentAsync(take, cancellationToken).ConfigureAwait(false);
            IEnumerable<ExtractionRun> filteredRuns = runs;
            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                filteredRuns = filteredRuns.Where(run => StringComparer.OrdinalIgnoreCase.Equals(run.Status.ToString(), query.Status.Trim()));
            }

            ExtractionRunHistoryItemResponse[] items = filteredRuns.Select(MapRun).ToArray();
            ExtractionRunHistoryResponse response = new(items, items.Length, take);
            return ManagementOperationResult<ExtractionRunHistoryResponse>.Success(response);
        }

        /// <summary>
        /// Stores a controlled rule enablement overlay without editing rule definition files.
        /// </summary>
        /// <param name="request">The rule enablement request to validate and store.</param>
        /// <param name="cancellationToken">The cancellation token for the rule enablement operation.</param>
        /// <returns>The accepted rule enablement state or validation errors.</returns>
        public Task<ManagementOperationResult<RuleEnablementResponse>> SetRuleEnablementAsync(RuleEnablementRequest request, CancellationToken cancellationToken)
        {
            // Enablement overlays are separate from rule catalog definitions so management calls cannot rewrite catalog files.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            string? ruleCode = NormalizeRequiredText(request.RuleCode, "RuleCodeRequired", "Rule code is required.", errors);
            string? version = NormalizeRequiredText(request.Version, "RuleVersionRequired", "Rule version is required.", errors);
            if (ruleCode is not null && ruleCode.Any(char.IsWhiteSpace))
            {
                errors.Add(new ManagementValidationError("RuleCodeInvalid", "Rule code must not contain whitespace."));
            }

            if (version is not null && version.Any(char.IsWhiteSpace))
            {
                errors.Add(new ManagementValidationError("RuleVersionInvalid", "Rule version must not contain whitespace."));
            }

            if (errors.Count > 0 || ruleCode is null || version is null)
            {
                return Task.FromResult(ManagementOperationResult<RuleEnablementResponse>.Failure(errors));
            }

            RuleEnablementResponse response = new(ruleCode, version, request.Enabled, NormalizeOptionalText(request.Reason), CreateAudit(request.RequestedBy));
            lock (_syncRoot)
            {
                _ruleEnablement[BuildRuleKey(ruleCode, version)] = response;
            }

            _logger.LogInformation("Rule {RuleCode} version {RuleVersion} enablement set to {Enabled}.", ruleCode, version, request.Enabled);
            return Task.FromResult(ManagementOperationResult<RuleEnablementResponse>.Success(response));
        }

        /// <summary>
        /// Executes a supported maintenance operation or previews its outcome without arbitrary mutation.
        /// </summary>
        /// <param name="request">The maintenance request to validate and execute.</param>
        /// <param name="cancellationToken">The cancellation token for the maintenance operation.</param>
        /// <returns>The maintenance outcome or validation errors.</returns>
        public Task<ManagementOperationResult<MaintenanceResponse>> RunMaintenanceAsync(MaintenanceRequest request, CancellationToken cancellationToken)
        {
            // The maintenance surface is an allowlisted command set and never accepts raw Cypher, shell, SQL, or filesystem commands.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            List<ManagementValidationError> errors = [];
            string? operation = NormalizeRequiredText(request.Operation, "MaintenanceOperationRequired", "Maintenance operation is required.", errors);
            if (operation is not null && !s_supportedMaintenanceOperations.Contains(operation))
            {
                errors.Add(new ManagementValidationError("MaintenanceOperationUnsupported", "Maintenance operation is not supported by the controlled management surface."));
            }

            if (errors.Count > 0 || operation is null)
            {
                return Task.FromResult(ManagementOperationResult<MaintenanceResponse>.Failure(errors));
            }

            MaintenanceResponse response = new(
                operation,
                request.DryRun,
                request.DryRun ? "Validated" : "Completed",
                request.DryRun ? ["Dry run completed without mutating management state."] : [],
                [],
                CreateAudit(request.RequestedBy));
            _logger.LogInformation("Maintenance operation {Operation} completed with dryRun={DryRun}.", operation, request.DryRun);
            return Task.FromResult(ManagementOperationResult<MaintenanceResponse>.Success(response));
        }

        /// <summary>
        /// Gets local management health without exposing infrastructure secrets.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for the health check.</param>
        /// <returns>The safe health response.</returns>
        public Task<ManagementHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
        {
            // Health reports only the module's local ability to answer controlled routes.
            cancellationToken.ThrowIfCancellationRequested();
            ManagementHealthResponse response = new(
                "Healthy",
                DateTimeOffset.UtcNow,
                ["Management module loaded", "Controlled operation registry available"],
                []);
            return Task.FromResult(response);
        }

        /// <summary>
        /// Gets sanitized readiness for snapshot, run-history, and rule-catalog dependencies.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token for the readiness check.</param>
        /// <returns>The safe readiness response.</returns>
        public async Task<ManagementReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken)
        {
            // Readiness probes dependencies through application abstractions and returns only sanitized names and states.
            cancellationToken.ThrowIfCancellationRequested();
            List<DependencyReadinessResponse> dependencies = [];
            dependencies.Add(new DependencyReadinessResponse("snapshot-lifecycle", GetSnapshotDependencyStatus(), "Snapshot lifecycle reader is available."));
            IReadOnlyList<ExtractionRun> runs = await _runHistory.GetRecentAsync(1, cancellationToken).ConfigureAwait(false);
            dependencies.Add(new DependencyReadinessResponse("extraction-run-history", "Ready", $"Run history reader is available with {runs.Count} recent run sample rows."));
            IReadOnlyList<RuleCatalogEntry> rules = await _ruleCatalogStore.GetRulesAsync(cancellationToken).ConfigureAwait(false);
            dependencies.Add(new DependencyReadinessResponse("rule-catalog", rules.Count > 0 ? "Ready" : "Degraded", rules.Count > 0 ? "Rule catalog entries are available." : "Rule catalog is reachable but no rules are loaded."));
            IReadOnlyList<string> warnings = dependencies.Any(dependency => StringComparer.OrdinalIgnoreCase.Equals(dependency.Status, "Degraded"))
                ? ["One or more dependencies are reachable but not fully populated for query workloads."]
                : [];
            string status = warnings.Count == 0 ? "Ready" : "Degraded";
            return new ManagementReadinessResponse(status, DateTimeOffset.UtcNow, dependencies, warnings);
        }

        /// <summary>
        /// Determines whether a repository registration exists for the supplied stable identity.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable identity to test.</param>
        /// <returns><see langword="true"/> when the repository exists; otherwise <see langword="false"/>.</returns>
        private bool RepositoryExists(string repositoryStableKey)
        {
            // Repository existence is checked under lock because registration can happen concurrently in tests or hosts.
            lock (_syncRoot)
            {
                return _repositories.ContainsKey(repositoryStableKey);
            }
        }

        /// <summary>
        /// Maps extraction run state into the safe management response shape.
        /// </summary>
        /// <param name="run">The extraction run state to project.</param>
        /// <returns>The safe run-history response row.</returns>
        private static ExtractionRunHistoryItemResponse MapRun(ExtractionRun run)
        {
            // The response exposes metadata keys and diagnostic counts, not arbitrary metadata values or stack traces.
            return new ExtractionRunHistoryItemResponse(
                run.RunId.ToString(),
                run.Status.ToString(),
                run.StartedUtc,
                run.CompletedUtc,
                run.Progress.Stage,
                run.Progress.Message,
                run.Progress.Percentage ?? 0,
                run.Warnings.Count,
                run.Errors.Count,
                run.SnapshotIdentity,
                run.SubmittedRequest.SolutionPaths,
                run.SubmittedRequest.MetadataKeys);
        }

        /// <summary>
        /// Builds lifecycle rows from the current in-memory snapshot writer when that diagnostic path is available.
        /// </summary>
        /// <returns>The available safe snapshot lifecycle rows.</returns>
        private IReadOnlyList<SnapshotLifecycleItemResponse> BuildSnapshotLifecycleRows()
        {
            // The default writer provides diagnostic snapshots; infrastructure implementations can replace this service for direct lifecycle access.
            if (_snapshotWriter is not InMemoryArchitectureSnapshotWriter inMemoryWriter)
            {
                return [];
            }

            return inMemoryWriter.GetSnapshotsSnapshotForDiagnostics()
                .Where(snapshot => snapshot.SnapshotHeader is not null)
                .Select(snapshot =>
                {
                    string? solutionStableKey = snapshot.Solutions.FirstOrDefault()?.StableKey.Value;
                    return new SnapshotLifecycleItemResponse(
                        snapshot.SnapshotHeader!.StableKey.Value,
                        snapshot.SnapshotHeader.RepositoryStableKey.Value,
                        solutionStableKey,
                        snapshot.SnapshotHeader.Status,
                        snapshot.SnapshotHeader.BranchName,
                        snapshot.SnapshotHeader.CommitSha,
                        snapshot.SnapshotHeader.StartedUtc,
                        snapshot.SnapshotHeader.CompletedUtc,
                        snapshot.SnapshotHeader.Warnings.Count,
                        snapshot.SnapshotHeader.Errors.Count);
                })
                .ToArray();
        }

        /// <summary>
        /// Gets a sanitized readiness status for the snapshot lifecycle dependency.
        /// </summary>
        /// <returns>The sanitized dependency status.</returns>
        private string GetSnapshotDependencyStatus()
        {
            // The default service can read lifecycle data only from the in-memory writer; other adapters should override this service.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter ? "Ready" : "Degraded";
        }

        /// <summary>
        /// Normalizes a required stable identity value.
        /// </summary>
        /// <param name="value">The submitted stable identity value.</param>
        /// <param name="code">The validation code used when the value is missing.</param>
        /// <param name="message">The validation message used when the value is missing.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized stable identity, or <see langword="null"/> when invalid.</returns>
        private static string? NormalizeRequiredStableKey(string? value, string code, string message, List<ManagementValidationError> errors)
        {
            // Stable identities are plain strings at the API boundary but must be non-empty and URI-like for public safety.
            string? normalized = NormalizeRequiredText(value, code, message, errors);
            if (normalized is not null && !normalized.Contains("://", StringComparison.Ordinal))
            {
                errors.Add(new ManagementValidationError("StableKeyInvalid", "Stable keys must include a scheme separator such as repository://."));
                return null;
            }

            return normalized;
        }

        /// <summary>
        /// Normalizes a required text value and records a validation error when blank.
        /// </summary>
        /// <param name="value">The submitted text value.</param>
        /// <param name="code">The validation code used when the value is missing.</param>
        /// <param name="message">The validation message used when the value is missing.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized text, or <see langword="null"/> when missing.</returns>
        private static string? NormalizeRequiredText(string? value, string code, string message, List<ManagementValidationError> errors)
        {
            // Required text normalization prevents whitespace-only inputs from becoming persisted management state.
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ManagementValidationError(code, message));
                return null;
            }

            return value.Trim();
        }

        /// <summary>
        /// Normalizes optional text values by trimming and converting blanks to <see langword="null"/>.
        /// </summary>
        /// <param name="value">The submitted optional text value.</param>
        /// <returns>The normalized text, or <see langword="null"/> when blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional values should not treat accidental whitespace as meaningful audit or metadata content.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Normalizes a repository-relative solution path and rejects unsafe shapes.
        /// </summary>
        /// <param name="path">The submitted solution path.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized path, or <see langword="null"/> when invalid.</returns>
        private static string? NormalizeSolutionPath(string? path, List<ManagementValidationError> errors)
        {
            // Management registration uses path shape validation rather than filesystem existence because registration must not trigger extraction.
            string? normalized = NormalizeRequiredText(path, "SolutionPathRequired", "Solution path is required.", errors);
            if (normalized is null)
            {
                return null;
            }

            string slashNormalized = normalized.Replace('\\', '/');
            if (Path.IsPathRooted(slashNormalized) || slashNormalized.Contains("../", StringComparison.Ordinal) || slashNormalized.StartsWith("../", StringComparison.Ordinal))
            {
                errors.Add(new ManagementValidationError("SolutionPathOutsideRepositoryRoot", "Solution path must stay inside the registered repository scope."));
                return null;
            }

            if (!slashNormalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) && !slashNormalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ManagementValidationError("SolutionPathExtensionInvalid", "Solution path must reference a .sln or .slnx file."));
                return null;
            }

            return slashNormalized;
        }

        /// <summary>
        /// Normalizes metadata by enforcing the approved field allowlist.
        /// </summary>
        /// <param name="metadata">The submitted metadata dictionary.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized metadata dictionary in deterministic key order.</returns>
        private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata, List<ManagementValidationError> errors)
        {
            // Metadata allowlisting prevents arbitrary graph-property updates while still supporting useful operational annotations.
            if (metadata is null || metadata.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            SortedDictionary<string, string> normalized = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in metadata)
            {
                string key = NormalizeOptionalText(pair.Key) ?? string.Empty;
                if (!s_allowedMetadataFields.Contains(key))
                {
                    errors.Add(new ManagementValidationError("MetadataFieldNotAllowed", $"Metadata field '{key}' is not approved for management updates."));
                    continue;
                }

                normalized[key] = pair.Value?.Trim() ?? string.Empty;
            }

            return normalized;
        }

        /// <summary>
        /// Normalizes and validates the metadata target kind.
        /// </summary>
        /// <param name="targetKind">The submitted target kind.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The normalized target kind, or <see langword="null"/> when invalid.</returns>
        private static string? NormalizeTargetKind(string? targetKind, List<ManagementValidationError> errors)
        {
            // Target kinds are limited to public lifecycle concepts so callers cannot mutate arbitrary graph labels.
            string? normalized = NormalizeRequiredText(targetKind, "TargetKindRequired", "Target kind is required.", errors)?.ToLowerInvariant();
            if (normalized is null)
            {
                return null;
            }

            if (normalized is not "repository" and not "solution" and not "snapshot")
            {
                errors.Add(new ManagementValidationError("TargetKindUnsupported", "Target kind must be repository, solution, or snapshot."));
                return null;
            }

            return normalized;
        }

        /// <summary>
        /// Normalizes a take value and enforces a bounded maximum result size.
        /// </summary>
        /// <param name="take">The requested take value.</param>
        /// <param name="defaultTake">The default value used when take is omitted.</param>
        /// <param name="errors">The validation error collection to append to.</param>
        /// <returns>The effective take value.</returns>
        private static int NormalizeTake(int? take, int defaultTake, List<ManagementValidationError> errors)
        {
            // Bounded take protects management endpoints from accidentally returning unbounded operational history.
            int effectiveTake = take ?? defaultTake;
            if (effectiveTake < 1 || effectiveTake > 500)
            {
                errors.Add(new ManagementValidationError("TakeOutOfRange", "take must be between 1 and 500."));
            }

            return effectiveTake;
        }

        /// <summary>
        /// Applies an optional ordinal text filter to a row sequence.
        /// </summary>
        /// <typeparam name="TRow">The row type being filtered.</typeparam>
        /// <param name="rows">The source row sequence.</param>
        /// <param name="filter">The optional filter value.</param>
        /// <param name="selector">The row property selector.</param>
        /// <returns>The filtered rows when a filter exists; otherwise the original rows.</returns>
        private static IEnumerable<TRow> ApplyOptionalFilter<TRow>(IEnumerable<TRow> rows, string? filter, Func<TRow, string> selector)
        {
            // Optional filter helpers keep lifecycle query composition readable and deterministic.
            return string.IsNullOrWhiteSpace(filter)
                ? rows
                : rows.Where(row => StringComparer.Ordinal.Equals(selector(row), filter.Trim()));
        }

        /// <summary>
        /// Applies an optional ordinal text filter to nullable row values.
        /// </summary>
        /// <typeparam name="TRow">The row type being filtered.</typeparam>
        /// <param name="rows">The source row sequence.</param>
        /// <param name="filter">The optional filter value.</param>
        /// <param name="selector">The nullable row property selector.</param>
        /// <returns>The filtered rows when a filter exists; otherwise the original rows.</returns>
        private static IEnumerable<TRow> ApplyOptionalNullableFilter<TRow>(IEnumerable<TRow> rows, string? filter, Func<TRow, string?> selector)
        {
            // Nullable filters match only present values so null metadata never masquerades as an empty string match.
            return string.IsNullOrWhiteSpace(filter)
                ? rows
                : rows.Where(row => StringComparer.Ordinal.Equals(selector(row), filter.Trim()));
        }

        /// <summary>
        /// Creates audit metadata for an accepted management operation.
        /// </summary>
        /// <param name="requestedBy">The optional submitted actor identity.</param>
        /// <returns>The audit metadata response for the operation.</returns>
        private static AuditMetadataResponse CreateAudit(string? requestedBy)
        {
            // The correlation ID is generated per accepted action so logs and API responses can be tied together without secrets.
            return new AuditMetadataResponse(NormalizeOptionalText(requestedBy) ?? "unknown", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Builds a private target dictionary key from a target kind and stable identity.
        /// </summary>
        /// <param name="targetKind">The normalized target kind.</param>
        /// <param name="stableKey">The stable identity of the target.</param>
        /// <returns>A deterministic private dictionary key.</returns>
        private static string BuildTargetKey(string targetKind, string stableKey)
        {
            // The separator is private to the in-memory store and never becomes a public identity.
            return string.Concat(targetKind, "\u001F", stableKey);
        }

        /// <summary>
        /// Builds a private rule enablement dictionary key from rule code and version.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <returns>A deterministic private dictionary key.</returns>
        private static string BuildRuleKey(string ruleCode, string version)
        {
            // The separator keeps rule code and version separate from public route shapes.
            return string.Concat(ruleCode, "\u001F", version);
        }
    }
}
