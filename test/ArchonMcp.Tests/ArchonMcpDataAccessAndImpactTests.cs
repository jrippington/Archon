using Archon.Application.Facts;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Traversal;
using Archon.Domain.Graph.Metadata;
using ArchonMcp.McpDataAccess;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpImpact;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 data-access usage and change-impact MCP tools across success, validation, truncation, redaction, and safe investigation guidance paths.
    /// </summary>
    public sealed class ArchonMcpDataAccessAndImpactTests
    {
        /// <summary>
        /// Confirms data-access usage returns persisted data-access facts with filters, operation kinds, dynamic SQL uncertainty, evidence, and secret-safe metadata.
        /// </summary>
        [Fact]
        public async Task GetDataAccessUsageReturnsFilteredEvidenceBackedFacts()
        {
            // The fake fact service captures the query so the test can prove MCP filters are mapped to the approved query abstraction.
            FakeFactQueryService factService = new(CreateDataAccessResult(3));
            using WebApplication app = BuildDataAccessImpactApp(factService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1, notFound: false, unavailable: false)));
            IArchonMcpDataAccessTool tool = app.Services.GetRequiredService<IArchonMcpDataAccessTool>();

            object payload = await tool.GetDataAccessUsageAsync(CreateDataAccessRequest(limit: 2, family: "EFCore", table: "Orders"), CancellationToken.None);

            // A successful envelope should expose only stable data-access identities, bounded facts, and redacted evidence previews.
            ArchonMcpEnvelope<ArchonMcpDataAccessUsageFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpDataAccessUsageFacts>>(payload);
            Assert.Equal(ArchonMcpDataAccessOperations.GetDataAccessUsage, envelope.Operation);
            Assert.Equal("EFCore", factService.DataAccessQueries[0].Family);
            Assert.Equal("Orders", factService.DataAccessQueries[0].Table);
            Assert.Equal(2, envelope.Facts.Usages.Count);
            Assert.Contains(envelope.Facts.Usages, usage => usage.OperationKinds.Contains("Read", StringComparer.OrdinalIgnoreCase));
            Assert.Contains(envelope.Facts.Usages, usage => usage.DynamicSqlIndicator);
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "dynamicSql");
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://data/orders-query");
            Assert.DoesNotContain("SuperSecret", string.Join(' ', envelope.Evidence.Select(evidence => evidence.SnippetPreview)), StringComparison.OrdinalIgnoreCase);
            Assert.True(envelope.Limits.Truncated);
        }

        /// <summary>
        /// Confirms invalid data-access filters fail validation before the fact query service is invoked.
        /// </summary>
        [Fact]
        public async Task GetDataAccessUsageValidationFailureDoesNotInvokeQueryLayer()
        {
            // The request intentionally supplies an internal-looking project identity to prove stable-key validation runs first.
            FakeFactQueryService factService = new(CreateDataAccessResult(1));
            using WebApplication app = BuildDataAccessImpactApp(factService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1, notFound: false, unavailable: false)));
            IArchonMcpDataAccessTool tool = app.Services.GetRequiredService<IArchonMcpDataAccessTool>();

            object payload = await tool.GetDataAccessUsageAsync(CreateDataAccessRequest(projectStableKey: "123"), CancellationToken.None);

            // The validation response and zero captured queries prove malformed input never reached the application layer.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(factService.DataAccessQueries);
        }

        /// <summary>
        /// Confirms disabled data-access usage fails closed before validation or query execution.
        /// </summary>
        [Fact]
        public async Task DisabledGetDataAccessUsageReturnsForbiddenBeforeQueryLayerIsInvoked()
        {
            // The operation allow-list intentionally omits the data-access tool to verify authorization remains the first gate.
            FakeFactQueryService factService = new(CreateDataAccessResult(1));
            using WebApplication app = BuildDataAccessImpactApp(factService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1, notFound: false, unavailable: false)), allowedOperations: ["archon.health"]);
            IArchonMcpDataAccessTool tool = app.Services.GetRequiredService<IArchonMcpDataAccessTool>();

            object payload = await tool.GetDataAccessUsageAsync(CreateDataAccessRequest(), CancellationToken.None);

            // Forbidden output and no captured queries prove the shared operation executor is still the first behavioral boundary.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(factService.DataAccessQueries);
        }

        /// <summary>
        /// Confirms change-impact assessment aggregates direct and transitive impact relationships with safe follow-up MCP calls.
        /// </summary>
        [Fact]
        public async Task AssessChangeImpactReturnsDirectAndTransitiveInvestigationGuidance()
        {
            // The fake traversal service models a bounded incoming impact neighbourhood around one changed data-access target.
            FakeGraphTraversalQueryService traversalService = new(CreateTraversalResult(edgeCount: 3, notFound: false, unavailable: false));
            using WebApplication app = BuildDataAccessImpactApp(new FakeFactQueryService(CreateDataAccessResult(1)), traversalService);
            IArchonMcpImpactTool tool = app.Services.GetRequiredService<IArchonMcpImpactTool>();

            object payload = await tool.AssessChangeImpactAsync(CreateImpactRequest(maximumDepth: 2, limit: 2), CancellationToken.None);

            // Impact output should distinguish direct and transitive impacts and frame next steps as read-only investigation rather than remediation.
            ArchonMcpEnvelope<ArchonMcpChangeImpactFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpChangeImpactFacts>>(payload);
            Assert.Equal(ArchonMcpImpactOperations.AssessChangeImpact, envelope.Operation);
            Assert.Equal("dataaccess://orders/query", envelope.Facts.TargetStableKey);
            Assert.Single(envelope.Facts.DirectImpacts);
            Assert.Single(envelope.Facts.TransitiveImpacts);
            Assert.Contains(envelope.SuggestedFollowUps, followUp => followUp.Operation == "archon.get_dependents");
            Assert.DoesNotContain(envelope.SuggestedFollowUps, followUp => followUp.Label.Contains("modify", StringComparison.OrdinalIgnoreCase));
            Assert.True(envelope.Limits.Truncated);
            Assert.Single(traversalService.TraversalQueries);
            Assert.Equal("Incoming", traversalService.TraversalQueries[0].Direction);
        }

        /// <summary>
        /// Confirms change-impact assessment rejects unsupported target identities before graph traversal.
        /// </summary>
        [Fact]
        public async Task AssessChangeImpactRejectsUnsupportedTargetStableKey()
        {
            // Unsupported target schemes could imply arbitrary graph access, so MCP validation rejects them before traversal.
            FakeGraphTraversalQueryService traversalService = new(CreateTraversalResult(edgeCount: 1, notFound: false, unavailable: false));
            using WebApplication app = BuildDataAccessImpactApp(new FakeFactQueryService(CreateDataAccessResult(1)), traversalService);
            IArchonMcpImpactTool tool = app.Services.GetRequiredService<IArchonMcpImpactTool>();

            object payload = await tool.AssessChangeImpactAsync(CreateImpactRequest(targetStableKey: "filesystem://local/path"), CancellationToken.None);

            // Validation output and zero traversal queries prove unsupported targets are not interpreted by graph dependencies.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(traversalService.TraversalQueries);
        }

        /// <summary>
        /// Confirms missing impact targets and query failures map to safe structured MCP errors.
        /// </summary>
        [Fact]
        public async Task AssessChangeImpactMapsMissingTargetsAndQueryFailuresSafely()
        {
            // The first fake reports a missing target; the second throws to exercise safe query-layer failure mapping.
            using WebApplication missingApp = BuildDataAccessImpactApp(new FakeFactQueryService(CreateDataAccessResult(1)), new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 0, notFound: true, unavailable: false)));
            IArchonMcpImpactTool missingTool = missingApp.Services.GetRequiredService<IArchonMcpImpactTool>();
            object missingPayload = await missingTool.AssessChangeImpactAsync(CreateImpactRequest(), CancellationToken.None);

            using WebApplication failingApp = BuildDataAccessImpactApp(new FakeFactQueryService(CreateDataAccessResult(1)), new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 0, notFound: false, unavailable: false), throwOnTraverse: true));
            IArchonMcpImpactTool failingTool = failingApp.Services.GetRequiredService<IArchonMcpImpactTool>();
            object failingPayload = await failingTool.AssessChangeImpactAsync(CreateImpactRequest(), CancellationToken.None);

            // Public errors use coarse categories and omit exception details or persistence internals.
            ArchonMcpErrorResponse missingError = Assert.IsType<ArchonMcpErrorResponse>(missingPayload);
            Assert.Equal(ArchonMcpErrorCategory.NotFound, missingError.Error.Category);
            ArchonMcpErrorResponse failingError = Assert.IsType<ArchonMcpErrorResponse>(failingPayload);
            Assert.Equal(ArchonMcpErrorCategory.QueryLayerFailure, failingError.Error.Category);
            Assert.DoesNotContain("InvalidOperationException", failingError.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds an MCP host application with fake fact and traversal query services plus configurable security allow-list settings.
        /// </summary>
        /// <param name="factService">The fake fact query service registered for data-access tests.</param>
        /// <param name="traversalService">The fake traversal query service registered for impact tests.</param>
        /// <param name="allowedOperations">The optional operation allow-list used by security tests.</param>
        /// <returns>A configured web application exposing the Work Item 7 MCP services.</returns>
        private static WebApplication BuildDataAccessImpactApp(FakeFactQueryService factService, FakeGraphTraversalQueryService traversalService, string[]? allowedOperations = null)
        {
            // Tests use production MCP composition and replace only approved application/query seams.
            List<string> args =
            [
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                "Archon:Mcp:Security:TestCallerId=developer-1",
                "Archon:Mcp:Limits:MaxResultCount=2",
                "Archon:Mcp:Limits:MaxEvidenceCount=2",
                "Archon:Mcp:Limits:MaxTraversalDepth=3"
            ];
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpDataAccessOperations.GetDataAccessUsage, ArchonMcpImpactOperations.AssessChangeImpact];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder =>
            {
                // Replacing query services with fakes keeps assertions independent of persistence implementation details.
                builder.Services.AddSingleton<IFactQueryService>(factService);
                builder.Services.AddSingleton<IGraphTraversalQueryService>(traversalService);
            });
        }

        /// <summary>
        /// Creates a data-access usage request scoped to deterministic repository and snapshot identities.
        /// </summary>
        /// <param name="projectStableKey">The optional owning project stable-key filter.</param>
        /// <param name="family">The optional data-access family filter.</param>
        /// <param name="table">The optional table filter.</param>
        /// <param name="limit">The optional maximum number of data-access facts to return.</param>
        /// <returns>A data-access usage request for MCP handler tests.</returns>
        private static ArchonMcpDataAccessUsageRequest CreateDataAccessRequest(string? projectStableKey = "project://src/orders/orders.csproj", string? family = null, string? table = null, int? limit = null)
        {
            // Stable scope values satisfy common MCP validation and query-layer selector requirements.
            return new ArchonMcpDataAccessUsageRequest(
                projectStableKey,
                DataContextStableKey: null,
                Entity: null,
                table,
                StoredProcedure: null,
                family,
                limit,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a change-impact request scoped to deterministic repository and snapshot identities.
        /// </summary>
        /// <param name="targetStableKey">The target stable key being assessed.</param>
        /// <param name="maximumDepth">The optional traversal depth.</param>
        /// <param name="limit">The optional maximum number of impact records to return.</param>
        /// <returns>A change-impact request for MCP handler tests.</returns>
        private static ArchonMcpChangeImpactRequest CreateImpactRequest(string? targetStableKey = "dataaccess://orders/query", int? maximumDepth = 2, int? limit = null)
        {
            // Change-impact assessment starts from one supported stable target and walks incoming dependencies through the query seam.
            return new ArchonMcpChangeImpactRequest(
                targetStableKey,
                maximumDepth,
                EdgeKindFilters: ["Calls", "References"],
                limit,
                IncludeTransitive: true,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a deterministic data-access fact result containing the requested number of rows before MCP limiting.
        /// </summary>
        /// <param name="count">The number of data-access rows to include in the query-layer page.</param>
        /// <returns>A successful data-access fact result.</returns>
        private static DataAccessFactResult CreateDataAccessResult(int count)
        {
            // Rows cover EF Core, raw SQL, and stored procedure style facts so MCP mapping can expose operation and uncertainty semantics.
            DataAccessFactDto[] rows =
            [
                new DataAccessFactDto("dataaccess://orders/query", "EFCore", "OrdersDbContext.Orders", "project://src/orders/orders.csproj", "datactx://orders/db", "entity://orders/order", "table://dbo/orders", null, ["symbol://orders/repository/get"], ["Read"], ["evidence://data/orders-query"], 0.93m, false, null, GraphMetadata.From(new Dictionary<string, object?> { ["operationKind"] = "Read", ["dynamicSql"] = false })),
                new DataAccessFactDto("dataaccess://orders/dynamic-search", "RawSql", "Dynamic order search", "project://src/orders/orders.csproj", null, null, "table://dbo/orders", null, ["symbol://orders/repository/search"], ["Read", "Unknown"], ["evidence://data/orders-dynamic"], 0.61m, true, "Dynamic SQL target could not be fully resolved.", GraphMetadata.From(new Dictionary<string, object?> { ["operationKind"] = "Unknown", ["dynamicSql"] = true })),
                new DataAccessFactDto("dataaccess://orders/reprice", "StoredProcedure", "usp_RepriceOrders", "project://src/orders/orders.csproj", null, null, null, "storedprocedure://dbo/usp_RepriceOrders", ["symbol://orders/repository/reprice"], ["Execute"], ["evidence://data/orders-sproc"], 0.88m, false, null, GraphMetadata.From(new Dictionary<string, object?> { ["operationKind"] = "Execute", ["dynamicSql"] = false }))
            ];
            PagedQueryResult<DataAccessFactDto> page = new(rows.Take(count), count, 0, Math.Max(1, count));
            return new DataAccessFactResult(page, CreateFactContext());
        }

        /// <summary>
        /// Creates a deterministic fact-query context for data-access envelopes.
        /// </summary>
        /// <returns>A fact query context.</returns>
        private static FactQueryContext CreateFactContext()
        {
            // Context supplies snapshot identity plus one warning and one unknown used by the MCP response mapper.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new FactQueryContext(scope, snapshot, [new FactWarningDto("DataAccessPartial", "Data-access extraction returned bounded results.")], [new FactUnknownDto("dynamicSql", "Dynamic SQL targets can be unresolved.")]);
        }

        /// <summary>
        /// Creates a deterministic graph traversal result for impact handler tests.
        /// </summary>
        /// <param name="edgeCount">The number of impact edges to include before MCP limiting.</param>
        /// <param name="notFound">A value indicating whether the target should be reported as missing.</param>
        /// <param name="unavailable">A value indicating whether traversal scope should be reported as unavailable.</param>
        /// <returns>A graph traversal result.</returns>
        private static GraphTraversalResult CreateTraversalResult(int edgeCount, bool notFound, bool unavailable)
        {
            // Graph rows model direct and transitive consumers of a changed data-access fact.
            if (notFound)
            {
                return new GraphTraversalResult([new GraphTraversalValidationError(GraphTraversalValidationCodes.NodeNotFound, "Target node was not found.")]);
            }

            if (unavailable)
            {
                return new GraphTraversalResult([new GraphTraversalValidationError(GraphTraversalValidationCodes.SnapshotNotFound, "Snapshot was not found.")]);
            }

            GraphNodeDto target = new("dataaccess://orders/query", "DataAccessFact", "OrdersDbContext.Orders", "project://src/orders/orders.csproj", ["evidence://data/orders-query"], 0.93m, false, null);
            GraphNodeDto service = new("symbol://orders/service", "Method", "OrderService.GetOrders", "project://src/orders/orders.csproj", ["evidence://impact/service"], 0.9m, false, null);
            GraphNodeDto endpoint = new("endpoint://orders/get", "Endpoint", "GET /orders", "project://src/orders/orders.csproj", ["evidence://impact/endpoint"], 0.86m, false, null);
            GraphNodeDto worker = new("worker://orders/sync", "Worker", "OrderSyncWorker", "project://src/orders/orders.csproj", ["evidence://impact/worker"], 0.74m, true, "Worker scheduling metadata is partial.");
            GraphEdgeDto[] edges =
            [
                new GraphEdgeDto("edge://service-uses-data", "UsesData", service.StableKey, target.StableKey, true, ["evidence://impact/service"], 0.9m, false, null),
                new GraphEdgeDto("edge://endpoint-calls-service", "Calls", endpoint.StableKey, service.StableKey, true, ["evidence://impact/endpoint"], 0.86m, false, null),
                new GraphEdgeDto("edge://worker-calls-service", "Calls", worker.StableKey, service.StableKey, true, ["evidence://impact/worker"], 0.74m, true, "Worker impact confidence is partial.")
            ];
            GraphEdgeDto[] selectedEdges = edges.Take(edgeCount).ToArray();
            GraphNodeDto[] nodes = [target, service, endpoint, worker];
            GraphTraversalResponseDto response = new(target.StableKey, "Incoming", "ChangeImpact", 2, ["Calls", "References"], nodes, selectedEdges, new GraphTraversalTruncationDto(false, 100, selectedEdges.Length, null));
            return new GraphTraversalResult(response, CreateTraversalContext());
        }

        /// <summary>
        /// Creates deterministic traversal query context for impact envelopes.
        /// </summary>
        /// <returns>A traversal query context.</returns>
        private static GraphTraversalQueryContext CreateTraversalContext()
        {
            // Context supplies snapshot identity and safe diagnostics shared by impact responses.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new GraphTraversalQueryContext(scope, snapshot, [new GraphTraversalWarningDto("ImpactPartial", "Impact traversal is bounded.")], [new GraphTraversalUnknownDto("runtimeDispatch", "Dynamic dispatch may hide additional consumers.")]);
        }

        /// <summary>
        /// Provides a controllable fact query service for MCP data-access tests.
        /// </summary>
        private sealed class FakeFactQueryService : IFactQueryService
        {
            /// <summary>
            /// Stores the data-access result returned by fake data-access queries.
            /// </summary>
            private readonly DataAccessFactResult _dataAccessResult;

            /// <summary>
            /// Stores data-access queries received by the fake service for assertions.
            /// </summary>
            private readonly List<DataAccessFactQuery> _dataAccessQueries = [];

            /// <summary>
            /// Initializes a fake fact service with a configured data-access result.
            /// </summary>
            /// <param name="dataAccessResult">The result returned when data-access facts are requested.</param>
            public FakeFactQueryService(DataAccessFactResult dataAccessResult)
            {
                // Work Item 7 tests use only data-access fact queries; other fact families fail loudly if called unexpectedly.
                _dataAccessResult = dataAccessResult ?? throw new ArgumentNullException(nameof(dataAccessResult));
            }

            /// <summary>
            /// Gets data-access queries received by the fake service.
            /// </summary>
            public IReadOnlyList<DataAccessFactQuery> DataAccessQueries => _dataAccessQueries;

            /// <inheritdoc />
            public Task<DataAccessFactResult> ListDataAccessFactsAsync(DataAccessFactQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves MCP filter mapping passes through the approved fact-query seam.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _dataAccessQueries.Add(query);
                return Task.FromResult(_dataAccessResult);
            }

            /// <inheritdoc />
            public Task<ConfigurationUsageResult> ListConfigurationUsageAsync(ConfigurationUsageQuery query, CancellationToken cancellationToken)
            {
                // Configuration facts are not part of the Work Item 7 MCP data-access handler contract.
                throw new NotSupportedException("Configuration usage is not used by MCP data-access tests.");
            }

            /// <inheritdoc />
            public Task<IntegrationFactResult> ListIntegrationFactsAsync(IntegrationFactQuery query, CancellationToken cancellationToken)
            {
                // Integration facts are not part of the Work Item 7 MCP data-access handler contract.
                throw new NotSupportedException("Integration facts are not used by MCP data-access tests.");
            }

            /// <inheritdoc />
            public Task<UiTechnologyFactResult> ListUiTechnologyFactsAsync(UiTechnologyFactQuery query, CancellationToken cancellationToken)
            {
                // UI technology facts are not part of the Work Item 7 MCP data-access handler contract.
                throw new NotSupportedException("UI technology facts are not used by MCP data-access tests.");
            }
        }

        /// <summary>
        /// Provides a controllable graph traversal query service for MCP impact tests.
        /// </summary>
        private sealed class FakeGraphTraversalQueryService : IGraphTraversalQueryService
        {
            /// <summary>
            /// Stores the traversal result returned by fake impact traversals.
            /// </summary>
            private readonly GraphTraversalResult _traversalResult;

            /// <summary>
            /// Stores a value indicating whether traversal should throw to exercise query-failure handling.
            /// </summary>
            private readonly bool _throwOnTraverse;

            /// <summary>
            /// Stores traversal queries received by the fake service for assertions.
            /// </summary>
            private readonly List<GraphTraversalQuery> _traversalQueries = [];

            /// <summary>
            /// Initializes a fake traversal service with configured behavior.
            /// </summary>
            /// <param name="traversalResult">The result returned when graph traversal is requested.</param>
            /// <param name="throwOnTraverse">A value indicating whether traversal should throw instead of returning a result.</param>
            public FakeGraphTraversalQueryService(GraphTraversalResult traversalResult, bool throwOnTraverse = false)
            {
                // The fake supports only ordinary traversal used by impact assessment; path search fails loudly if called unexpectedly.
                _traversalResult = traversalResult ?? throw new ArgumentNullException(nameof(traversalResult));
                _throwOnTraverse = throwOnTraverse;
            }

            /// <summary>
            /// Gets traversal queries received by the fake service.
            /// </summary>
            public IReadOnlyList<GraphTraversalQuery> TraversalQueries => _traversalQueries;

            /// <inheritdoc />
            public Task<GraphTraversalResult> TraverseAsync(GraphTraversalQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves MCP impact mapping uses incoming traversal over the approved graph abstraction.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _traversalQueries.Add(query);
                if (_throwOnTraverse)
                {
                    throw new InvalidOperationException("Simulated traversal failure with internal details.");
                }

                return Task.FromResult(_traversalResult);
            }

            /// <inheritdoc />
            public Task<DependencyPathResult> GetDependencyPathAsync(DependencyPathQuery query, CancellationToken cancellationToken)
            {
                // Dependency path search is not expected from change-impact assessment.
                throw new NotSupportedException("Dependency path search is not used by MCP impact tests.");
            }
        }
    }
}
