using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Application.Management;
using Archon.Application.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Application.Tests.Management
{
    /// <summary>
    /// Verifies management application behavior that coordinates lifecycle queries with operational readiness and responses.
    /// </summary>
    public sealed class ManagementOperationsServiceTests
    {
        /// <summary>
        /// Confirms lifecycle listing delegates filtering and ordering to the lifecycle query port while preserving the response contract.
        /// </summary>
        /// <returns>A task that completes after the lifecycle response is asserted.</returns>
        [Fact]
        public async Task ListSnapshotsAsync_WhenLifecycleRowsExist_ShouldReturnRowsFromLifecycleQueryPort()
        {
            // The fake query proves the service no longer inspects an in-memory snapshot writer to construct lifecycle rows.
            FakeSnapshotLifecycleQuery lifecycleQuery = new(new SnapshotLifecycleQueryResult(
                [new SnapshotLifecycleQueryRow("snapshot://one", "repository://one", "solution://one", "Completed", "main", "abc123", DateTimeOffset.Parse("2026-05-20T08:00:00Z"), DateTimeOffset.Parse("2026-05-20T08:05:00Z"), 1, 0)],
                TotalCount: 1,
                Take: 25,
                Warnings: []));
            ManagementOperationsService service = CreateService(lifecycleQuery);

            ManagementOperationResult<SnapshotLifecycleResponse> result = await service.ListSnapshotsAsync(new SnapshotLifecycleQuery("repository://one", null, "Completed", null, null, "abc123", 25), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            SnapshotLifecycleItemResponse item = Assert.Single(result.Data.Items);
            Assert.Equal("snapshot://one", item.SnapshotStableKey);
            Assert.Equal("repository://one", lifecycleQuery.LastQuery?.RepositoryStableKey);
            Assert.Equal("Completed", lifecycleQuery.LastQuery?.Status);
            Assert.Equal("abc123", lifecycleQuery.LastQuery?.CommitSha);
            Assert.Equal(25, lifecycleQuery.LastQuery?.Take);
        }

        /// <summary>
        /// Confirms readiness probes snapshot lifecycle through the application port and reports sanitized dependency state.
        /// </summary>
        /// <returns>A task that completes after readiness dependencies are asserted.</returns>
        [Fact]
        public async Task GetReadinessAsync_WhenLifecyclePortIsAvailable_ShouldReportSnapshotLifecycleReady()
        {
            // Readiness should mention the public dependency name and should not depend on or reveal an in-memory writer implementation detail.
            FakeSnapshotLifecycleQuery lifecycleQuery = new(new SnapshotLifecycleQueryResult([], TotalCount: 0, Take: 1, Warnings: []));
            ManagementOperationsService service = CreateService(lifecycleQuery);

            ManagementReadinessResponse readiness = await service.GetReadinessAsync(CancellationToken.None);

            DependencyReadinessResponse dependency = Assert.Single(readiness.Dependencies, item => item.Name == "snapshot-lifecycle");
            Assert.Equal("Ready", dependency.Status);
            Assert.DoesNotContain("in-memory", dependency.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(lifecycleQuery.LastQuery);
            Assert.Equal(1, lifecycleQuery.LastQuery.Take);
        }

        /// <summary>
        /// Confirms delete-one snapshot validation delegates destructive work to the deletion port and returns safe audit-ready counts.
        /// </summary>
        /// <returns>A task that completes after deletion response fields and delegated request values are asserted.</returns>
        [Fact]
        public async Task DeleteSnapshotAsync_WhenSnapshotExists_ShouldReturnDeletionCountsFromDeletionStore()
        {
            // The fake store proves the service validates stable-key input and does not perform storage-specific deletion directly.
            FakeSnapshotDeletionStore deletionStore = new(new SnapshotDeletionResult("snapshot://delete-one", true, 1, 4, 7, 1, ["Run history was preserved."]));
            ManagementOperationsService service = CreateService(new FakeSnapshotLifecycleQuery(new SnapshotLifecycleQueryResult([], 0, 1, [])), deletionStore);

            ManagementOperationResult<DeleteSnapshotResponse> result = await service.DeleteSnapshotAsync(new DeleteSnapshotRequest(" snapshot://delete-one ", "operator"), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.Equal("snapshot://delete-one", deletionStore.LastRequest?.SnapshotStableKey);
            Assert.Equal(1, result.Data.DeletedSnapshotCount);
            Assert.Equal(4, result.Data.DeletedNodeCount);
            Assert.Equal(7, result.Data.DeletedRelationshipCount);
            Assert.Equal(1, result.Data.AffectedRunCount);
            Assert.Equal("operator", result.Data.Audit.RequestedBy);
        }

        /// <summary>
        /// Confirms delete-one snapshot rejects invalid stable keys before invoking destructive storage behavior.
        /// </summary>
        /// <returns>A task that completes after validation and no-call behavior are asserted.</returns>
        [Fact]
        public async Task DeleteSnapshotAsync_WhenSnapshotStableKeyIsInvalid_ShouldReturnValidationProblemWithoutDeleting()
        {
            // Invalid stable keys cannot reach the deletion port, which prevents arbitrary mutation expressions from being used as identities.
            FakeSnapshotDeletionStore deletionStore = new(new SnapshotDeletionResult("snapshot://unused", true, 1, 1, 1, 0, []));
            ManagementOperationsService service = CreateService(new FakeSnapshotLifecycleQuery(new SnapshotLifecycleQueryResult([], 0, 1, [])), deletionStore);

            ManagementOperationResult<DeleteSnapshotResponse> result = await service.DeleteSnapshotAsync(new DeleteSnapshotRequest("not-a-stable-key", "operator"), CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Code == "StableKeyInvalid");
            Assert.Null(deletionStore.LastRequest);
        }

        /// <summary>
        /// Confirms delete-one snapshot maps storage not-found results to a safe validation error.
        /// </summary>
        /// <returns>A task that completes after the not-found result is asserted.</returns>
        [Fact]
        public async Task DeleteSnapshotAsync_WhenSnapshotDoesNotExist_ShouldReturnSnapshotNotFoundValidationError()
        {
            // Missing snapshots are reported as a controlled validation-style error so route responses stay safe and consistent.
            FakeSnapshotDeletionStore deletionStore = new(new SnapshotDeletionResult("snapshot://missing", false, 0, 0, 0, 0, []));
            ManagementOperationsService service = CreateService(new FakeSnapshotLifecycleQuery(new SnapshotLifecycleQueryResult([], 0, 1, [])), deletionStore);

            ManagementOperationResult<DeleteSnapshotResponse> result = await service.DeleteSnapshotAsync(new DeleteSnapshotRequest("snapshot://missing", "operator"), CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Code == "SnapshotNotFound");
            Assert.Equal("snapshot://missing", deletionStore.LastRequest?.SnapshotStableKey);
        }

        /// <summary>
        /// Confirms delete-all snapshot cleanup requires the exact confirmation phrase before invoking destructive storage behavior.
        /// </summary>
        /// <returns>A task that completes after validation and no-call behavior are asserted.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsAsync_WhenConfirmationIsMissing_ShouldReturnValidationProblemWithoutDeleting()
        {
            // The global cleanup path must not call the deletion store until the caller provides the explicit destructive confirmation phrase.
            FakeSnapshotDeletionStore deletionStore = new(new SnapshotDeleteAllResult(2, 10, 20, 1, []));
            ManagementOperationsService service = CreateService(new FakeSnapshotLifecycleQuery(new SnapshotLifecycleQueryResult([], 0, 1, [])), deletionStore);

            ManagementOperationResult<DeleteAllSnapshotsResponse> result = await service.DeleteAllSnapshotsAsync(new DeleteAllSnapshotsRequest(null, "operator"), CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Code == "DeleteAllSnapshotsConfirmationRequired");
            Assert.Null(deletionStore.LastDeleteAllRequest);
        }

        /// <summary>
        /// Confirms delete-all snapshot cleanup rejects incorrect confirmation values before invoking destructive storage behavior.
        /// </summary>
        /// <returns>A task that completes after validation and no-call behavior are asserted.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsAsync_WhenConfirmationIsIncorrect_ShouldReturnValidationProblemWithoutDeleting()
        {
            // A near-miss phrase is rejected so clients cannot accidentally trigger global cleanup through casual wording.
            FakeSnapshotDeletionStore deletionStore = new(new SnapshotDeleteAllResult(2, 10, 20, 1, []));
            ManagementOperationsService service = CreateService(new FakeSnapshotLifecycleQuery(new SnapshotLifecycleQueryResult([], 0, 1, [])), deletionStore);

            ManagementOperationResult<DeleteAllSnapshotsResponse> result = await service.DeleteAllSnapshotsAsync(new DeleteAllSnapshotsRequest("delete snapshots", "operator"), CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Code == "DeleteAllSnapshotsConfirmationInvalid");
            Assert.Null(deletionStore.LastDeleteAllRequest);
        }

        /// <summary>
        /// Confirms delete-all snapshot cleanup delegates to the deletion port and maps aggregate safe counts into the response.
        /// </summary>
        /// <returns>A task that completes after response fields and delegated request values are asserted.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsAsync_WhenConfirmationIsValid_ShouldReturnAggregateCountsFromDeletionStore()
        {
            // The fake store proves the service owns confirmation validation while infrastructure owns destructive graph cleanup.
            FakeSnapshotDeletionStore deletionStore = new(new SnapshotDeleteAllResult(2, 10, 20, 1, ["Run history was preserved."]));
            ManagementOperationsService service = CreateService(new FakeSnapshotLifecycleQuery(new SnapshotLifecycleQueryResult([], 0, 1, [])), deletionStore);

            ManagementOperationResult<DeleteAllSnapshotsResponse> result = await service.DeleteAllSnapshotsAsync(new DeleteAllSnapshotsRequest(" delete-all-snapshots ", "operator"), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.Equal("delete-all-snapshots", deletionStore.LastDeleteAllRequest?.Confirmation);
            Assert.Equal(2, result.Data.DeletedSnapshotCount);
            Assert.Equal(10, result.Data.DeletedNodeCount);
            Assert.Equal(20, result.Data.DeletedRelationshipCount);
            Assert.Equal(1, result.Data.AffectedRunCount);
            Assert.Equal("operator", result.Data.Audit.RequestedBy);
        }

        /// <summary>
        /// Creates a management service with fake lifecycle storage and lightweight local fallback dependencies.
        /// </summary>
        /// <param name="lifecycleQuery">The fake lifecycle query injected into the service.</param>
        /// <returns>A management service ready for focused application tests.</returns>
        private static ManagementOperationsService CreateService(ISnapshotLifecycleQuery lifecycleQuery)
        {
            // The in-memory run history and rule catalog keep tests focused on lifecycle coordination rather than unrelated dependencies.
            InMemoryArchitectureSnapshotWriter snapshotWriter = new();
            return new ManagementOperationsService(
                lifecycleQuery,
                new InMemoryExtractionRunHistory(),
                new InMemorySnapshotDeletionStore(snapshotWriter),
                new InMemoryRuleCatalogStore(),
                NullLogger<ManagementOperationsService>.Instance);
        }

        /// <summary>
        /// Creates a management service with fake lifecycle and deletion storage for delete-one tests.
        /// </summary>
        /// <param name="lifecycleQuery">The fake lifecycle query injected into the service.</param>
        /// <param name="deletionStore">The fake deletion store injected into the service.</param>
        /// <returns>A management service ready for focused application tests.</returns>
        private static ManagementOperationsService CreateService(ISnapshotLifecycleQuery lifecycleQuery, ISnapshotDeletionStore deletionStore)
        {
            // The helper keeps destructive-operation tests focused on validation and response mapping rather than unrelated dependencies.
            return new ManagementOperationsService(
                lifecycleQuery,
                new InMemoryExtractionRunHistory(),
                deletionStore,
                new InMemoryRuleCatalogStore(),
                NullLogger<ManagementOperationsService>.Instance);
        }

        /// <summary>
        /// Fake lifecycle query that records the normalized query supplied by the management service.
        /// </summary>
        private sealed class FakeSnapshotLifecycleQuery : ISnapshotLifecycleQuery
        {
            /// <summary>
            /// Stores the result returned for every fake lifecycle query.
            /// </summary>
            private readonly SnapshotLifecycleQueryResult _result;

            /// <summary>
            /// Initializes a new instance of the <see cref="FakeSnapshotLifecycleQuery"/> class.
            /// </summary>
            /// <param name="result">The lifecycle result returned to service callers.</param>
            public FakeSnapshotLifecycleQuery(SnapshotLifecycleQueryResult result)
            {
                // The result is supplied by each test so the fake can represent populated or empty storage without persistence setup.
                _result = result;
            }

            /// <summary>
            /// Gets the most recent normalized query observed by the fake.
            /// </summary>
            public SnapshotLifecycleQueryRequest? LastQuery { get; private set; }

            /// <summary>
            /// Records the query and returns the configured lifecycle result.
            /// </summary>
            /// <param name="query">The normalized lifecycle query supplied by the management service.</param>
            /// <param name="cancellationToken">The token that cancels the fake query before it records the request.</param>
            /// <returns>The configured lifecycle result.</returns>
            public Task<SnapshotLifecycleQueryResult> ListSnapshotsAsync(SnapshotLifecycleQueryRequest query, CancellationToken cancellationToken)
            {
                // Recording the query lets tests prove normalization and bounds are delegated to the port as intended.
                cancellationToken.ThrowIfCancellationRequested();
                LastQuery = query;
                return Task.FromResult(_result);
            }
        }

        /// <summary>
        /// Fake snapshot deletion store that records the normalized destructive requests supplied by the management service.
        /// </summary>
        private sealed class FakeSnapshotDeletionStore : ISnapshotDeletionStore
        {
            /// <summary>
            /// Stores the result returned for every fake deletion request.
            /// </summary>
            private readonly SnapshotDeletionResult? _deleteOneResult;

            /// <summary>
            /// Stores the result returned for every fake delete-all request.
            /// </summary>
            private readonly SnapshotDeleteAllResult? _deleteAllResult;

            /// <summary>
            /// Initializes a new instance of the <see cref="FakeSnapshotDeletionStore"/> class.
            /// </summary>
            /// <param name="result">The deletion result returned to service callers.</param>
            public FakeSnapshotDeletionStore(SnapshotDeletionResult result)
            {
                // The result is supplied by each test so the fake can represent success or not-found storage behavior.
                _deleteOneResult = result;
                _deleteAllResult = null;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="FakeSnapshotDeletionStore"/> class.
            /// </summary>
            /// <param name="result">The aggregate delete-all result returned to service callers.</param>
            public FakeSnapshotDeletionStore(SnapshotDeleteAllResult result)
            {
                // The result is supplied by each test so the fake can represent delete-all storage behavior without graph setup.
                _deleteOneResult = null;
                _deleteAllResult = result;
            }

            /// <summary>
            /// Gets the most recent normalized deletion request observed by the fake.
            /// </summary>
            public SnapshotDeletionRequest? LastRequest { get; private set; }

            /// <summary>
            /// Gets the most recent normalized delete-all request observed by the fake.
            /// </summary>
            public SnapshotDeleteAllRequest? LastDeleteAllRequest { get; private set; }

            /// <summary>
            /// Records the delete-one request and returns the configured result.
            /// </summary>
            /// <param name="request">The normalized delete-one request supplied by the management service.</param>
            /// <param name="cancellationToken">The token that cancels the fake deletion before it records the request.</param>
            /// <returns>The configured deletion result.</returns>
            public Task<SnapshotDeletionResult> DeleteSnapshotAsync(SnapshotDeletionRequest request, CancellationToken cancellationToken)
            {
                // Recording the request lets tests prove validation and normalization happen before destructive storage work.
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(_deleteOneResult);
                LastRequest = request;
                return Task.FromResult(_deleteOneResult);
            }

            /// <summary>
            /// Records the delete-all request and returns the configured aggregate result.
            /// </summary>
            /// <param name="request">The normalized delete-all request supplied by the management service.</param>
            /// <param name="cancellationToken">The token that cancels the fake deletion before it records the request.</param>
            /// <returns>The configured aggregate deletion result.</returns>
            public Task<SnapshotDeleteAllResult> DeleteAllSnapshotsAsync(SnapshotDeleteAllRequest request, CancellationToken cancellationToken)
            {
                // Recording the request lets tests prove confirmation validation happens before global destructive storage work.
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(_deleteAllResult);
                LastDeleteAllRequest = request;
                return Task.FromResult(_deleteAllResult);
            }
        }
    }
}
