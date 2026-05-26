using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Symbols;
using Archon.Application.Traversal;
using ArchonMcp.McpDependencies;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSymbols;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 dependency-path, symbol-description, and symbol-usage MCP tools across success, validation, security, ambiguity, and bounded-output paths.
    /// </summary>
    public sealed class ArchonMcpPathAndSymbolTests
    {
        /// <summary>
        /// Confirms dependency path search returns deterministic path facts, evidence references, and path-specific follow-ups through the approved traversal query seam.
        /// </summary>
        [Fact]
        public async Task FindDependencyPathsReturnsEvidenceBackedPathFacts()
        {
            // The fake traversal service proves the MCP tool delegates path discovery to the application traversal abstraction.
            FakeGraphTraversalQueryService traversalService = new(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 2));
            using WebApplication app = BuildPathSymbolApp(traversalService, new FakeSymbolQueryService(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 1)));
            IArchonMcpDependencyPathTool tool = app.Services.GetRequiredService<IArchonMcpDependencyPathTool>();

            object payload = await tool.FindDependencyPathsAsync(CreatePathRequest(maximumDepth: 3, limit: 1), CancellationToken.None);

            // A successful envelope must expose only stable graph identities and bounded path records.
            ArchonMcpEnvelope<ArchonMcpDependencyPathFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpDependencyPathFacts>>(payload);
            Assert.Equal(ArchonMcpDependencyPathOperation.Name, envelope.Operation);
            Assert.Equal("symbol://orders/create", envelope.Facts.SourceNodeStableKey);
            Assert.Equal("symbol://domain/order", envelope.Facts.TargetNodeStableKey);
            Assert.True(envelope.Facts.PathFound);
            Assert.Single(envelope.Facts.Paths);
            Assert.Equal(2, envelope.Facts.Paths[0].Edges.Count);
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://path/orders-to-domain");
            Assert.False(envelope.Limits.Truncated);
            Assert.Single(traversalService.PathQueries);
            Assert.Equal(3, traversalService.PathQueries[0].Depth);
        }

        /// <summary>
        /// Confirms dependency path search reports no-path as successful known absence rather than dependency-unavailable failure.
        /// </summary>
        [Fact]
        public async Task FindDependencyPathsDistinguishesNoPathFromUnavailableData()
        {
            // No-path results are successful query data with explicit unknown context, not validation or infrastructure failures.
            FakeGraphTraversalQueryService traversalService = new(CreatePathResult(pathFound: false, unavailable: false, edgeCount: 0));
            using WebApplication app = BuildPathSymbolApp(traversalService, new FakeSymbolQueryService(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 1)));
            IArchonMcpDependencyPathTool tool = app.Services.GetRequiredService<IArchonMcpDependencyPathTool>();

            object payload = await tool.FindDependencyPathsAsync(CreatePathRequest(), CancellationToken.None);

            // The response remains a success envelope and records noPath so clients do not confuse absence with missing graph data.
            ArchonMcpEnvelope<ArchonMcpDependencyPathFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpDependencyPathFacts>>(payload);
            Assert.False(envelope.Facts.PathFound);
            Assert.True(envelope.Facts.DataAvailable);
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "noDependencyPath");
            Assert.Contains(envelope.Warnings, warning => warning.Code == "mcp.archon.find_dependency_paths.no_path");
        }

        /// <summary>
        /// Confirms path validation prevents missing target identities from reaching the traversal query layer.
        /// </summary>
        [Fact]
        public async Task FindDependencyPathsValidationFailureDoesNotInvokeQueryLayer()
        {
            // Missing target identity is rejected at the MCP boundary before any graph traversal dependency can run.
            FakeGraphTraversalQueryService traversalService = new(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 1));
            using WebApplication app = BuildPathSymbolApp(traversalService, new FakeSymbolQueryService(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 1)));
            IArchonMcpDependencyPathTool tool = app.Services.GetRequiredService<IArchonMcpDependencyPathTool>();

            object payload = await tool.FindDependencyPathsAsync(CreatePathRequest(targetStableKey: null), CancellationToken.None);

            // A validation error and no captured path query prove malformed input did not reach application traversal.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(traversalService.PathQueries);
        }

        /// <summary>
        /// Confirms disabled path search fails closed before validation and query execution.
        /// </summary>
        [Fact]
        public async Task DisabledFindDependencyPathsReturnsForbiddenBeforeQueryLayerIsInvoked()
        {
            // The allow-list intentionally omits the path operation to verify authorization precedes all query work.
            FakeGraphTraversalQueryService traversalService = new(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 1));
            using WebApplication app = BuildPathSymbolApp(traversalService, new FakeSymbolQueryService(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 1)), allowedOperations: ["archon.health"]);
            IArchonMcpDependencyPathTool tool = app.Services.GetRequiredService<IArchonMcpDependencyPathTool>();

            object payload = await tool.FindDependencyPathsAsync(CreatePathRequest(), CancellationToken.None);

            // Forbidden output and zero queries prove the operation executor remains the first behavioral gate.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(traversalService.PathQueries);
        }

        /// <summary>
        /// Confirms symbol description maps identity, containment, project/source context, relationships, evidence, findings, unknowns, and redacted untrusted snippets.
        /// </summary>
        [Fact]
        public async Task DescribeSymbolReturnsRedactedEvidenceBackedSymbolFacts()
        {
            // The detail result contains a secret-like snippet so the MCP mapper must redact and label it as untrusted evidence data.
            FakeSymbolQueryService symbolService = new(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 1));
            using WebApplication app = BuildPathSymbolApp(new FakeGraphTraversalQueryService(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 1)), symbolService);
            IArchonMcpSymbolTool tool = app.Services.GetRequiredService<IArchonMcpSymbolTool>();

            object payload = await tool.DescribeSymbolAsync(CreateDescribeSymbolRequest(symbolStableKey: "symbol://orders/create"), CancellationToken.None);

            // The symbol envelope preserves semantic facts while avoiding raw secret exposure in previews.
            ArchonMcpEnvelope<ArchonMcpSymbolFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSymbolFacts>>(payload);
            Assert.Equal(ArchonMcpSymbolOperations.DescribeSymbol, envelope.Operation);
            Assert.Equal("symbol://orders/create", envelope.Facts.Identity.StableKey);
            Assert.Equal("project://src/orders/orders.csproj", envelope.Facts.ProjectStableKey);
            Assert.Contains(envelope.Facts.Relationships, relationship => relationship.StableKey == "edge://orders-create-calls-domain");
            Assert.DoesNotContain("SuperSecret", envelope.Facts.Source.SnippetPreview, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("untrusted-repository-evidence", envelope.Facts.Source.TrustLabel);
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://symbol/orders-create");
            Assert.Contains(envelope.Findings, finding => finding.StableKey == "rule://symbol/entrypoint");
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "semanticRole");
            Assert.Single(symbolService.DetailQueries);
        }

        /// <summary>
        /// Confirms ambiguous symbol text lookup returns a structured ambiguity error with safe candidate follow-up parameters.
        /// </summary>
        [Fact]
        public async Task DescribeSymbolReturnsAmbiguityErrorForAmbiguousSearchText()
        {
            // The query-layer ambiguity signal should not select a symbol arbitrarily.
            FakeSymbolQueryService symbolService = new(new SymbolDetailResult([new SymbolQueryValidationError(SymbolQueryValidationCodes.SymbolSearchTextAmbiguous, "Search text matched multiple symbols.")]), CreateSymbolUsageResult(usageCount: 1));
            using WebApplication app = BuildPathSymbolApp(new FakeGraphTraversalQueryService(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 1)), symbolService);
            IArchonMcpSymbolTool tool = app.Services.GetRequiredService<IArchonMcpSymbolTool>();

            object payload = await tool.DescribeSymbolAsync(CreateDescribeSymbolRequest(searchText: "Create"), CancellationToken.None);

            // The error category tells clients to retry with a stable key or perform a search-style disambiguation first.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Ambiguous, error.Error.Category);
            Assert.Contains(error.SuggestedFollowUps, followUp => followUp.Operation == "archon.search");
        }

        /// <summary>
        /// Confirms symbol usage returns callers and references with deterministic ordering, evidence references, untrusted snippets, and truncation metadata.
        /// </summary>
        [Fact]
        public async Task FindSymbolUsagesReturnsBoundedUsageFacts()
        {
            // Three usages with a two-item configured limit should return truncation metadata and preserve stable usage identities.
            FakeSymbolQueryService symbolService = new(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 3));
            using WebApplication app = BuildPathSymbolApp(new FakeGraphTraversalQueryService(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 1)), symbolService);
            IArchonMcpSymbolTool tool = app.Services.GetRequiredService<IArchonMcpSymbolTool>();

            object payload = await tool.FindSymbolUsagesAsync(CreateSymbolUsageRequest(limit: 2), CancellationToken.None);

            // Usage facts are bounded by MCP limits and include safe follow-ups for further read-only investigation.
            ArchonMcpEnvelope<ArchonMcpSymbolUsageFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSymbolUsageFacts>>(payload);
            Assert.Equal(ArchonMcpSymbolOperations.FindSymbolUsages, envelope.Operation);
            Assert.Equal("symbol://orders/create", envelope.Facts.SymbolStableKey);
            Assert.Equal(2, envelope.Facts.Usages.Count);
            Assert.Contains(envelope.Facts.Usages, usage => usage.UsageKind == "Calls");
            Assert.DoesNotContain("abc", string.Join(' ', envelope.Facts.Usages.Select(usage => usage.SnippetPreview)), StringComparison.OrdinalIgnoreCase);
            Assert.True(envelope.Limits.Truncated);
            Assert.Contains(envelope.Warnings, warning => warning.Code == "mcp.archon.find_symbol_usages.truncated");
            Assert.Single(symbolService.UsageQueries);
            Assert.Equal("Incoming", symbolService.UsageQueries[0].Direction);
        }

        /// <summary>
        /// Confirms symbol usage validation failures prevent query-layer invocation.
        /// </summary>
        [Fact]
        public async Task FindSymbolUsagesValidationFailureDoesNotInvokeQueryLayer()
        {
            // Missing stable symbol identity cannot be safely resolved by the usage query, so MCP validation stops first.
            FakeSymbolQueryService symbolService = new(CreateSymbolDetailResult(), CreateSymbolUsageResult(usageCount: 1));
            using WebApplication app = BuildPathSymbolApp(new FakeGraphTraversalQueryService(CreatePathResult(pathFound: true, unavailable: false, edgeCount: 1)), symbolService);
            IArchonMcpSymbolTool tool = app.Services.GetRequiredService<IArchonMcpSymbolTool>();

            object payload = await tool.FindSymbolUsagesAsync(CreateSymbolUsageRequest(symbolStableKey: null), CancellationToken.None);

            // A validation error and zero usage queries prove invalid usage input stays at the MCP boundary.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(symbolService.UsageQueries);
        }

        /// <summary>
        /// Builds an MCP host application with fake traversal and symbol services plus configurable security allow-list settings.
        /// </summary>
        /// <param name="traversalService">The fake traversal query service registered for dependency-path tests.</param>
        /// <param name="symbolService">The fake symbol query service registered for symbol tests.</param>
        /// <param name="allowedOperations">The optional operation allow-list used by security tests.</param>
        /// <returns>A configured web application exposing the Work Item 6 MCP services.</returns>
        private static WebApplication BuildPathSymbolApp(FakeGraphTraversalQueryService traversalService, FakeSymbolQueryService symbolService, string[]? allowedOperations = null)
        {
            // Tests use production MCP composition and replace only the approved application/query seams.
            List<string> args =
            [
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                "Archon:Mcp:Security:TestCallerId=developer-1",
                "Archon:Mcp:Limits:MaxResultCount=2",
                "Archon:Mcp:Limits:MaxPathCount=1"
            ];
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpDependencyPathOperation.Name, ArchonMcpSymbolOperations.DescribeSymbol, ArchonMcpSymbolOperations.FindSymbolUsages];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder =>
            {
                // Replacing query services with fakes makes each assertion independent of persistence implementation details.
                builder.Services.AddSingleton<IGraphTraversalQueryService>(traversalService);
                builder.Services.AddSingleton<ISymbolQueryService>(symbolService);
            });
        }

        /// <summary>
        /// Creates a dependency-path request scoped to deterministic test repository and snapshot identities.
        /// </summary>
        /// <param name="sourceStableKey">The source node stable key, or <see langword="null" /> to test validation.</param>
        /// <param name="targetStableKey">The target node stable key, or <see langword="null" /> to test validation.</param>
        /// <param name="maximumDepth">The optional maximum path-search depth.</param>
        /// <param name="limit">The optional path count limit.</param>
        /// <returns>A dependency-path request for MCP handler tests.</returns>
        private static ArchonMcpDependencyPathRequest CreatePathRequest(string? sourceStableKey = "symbol://orders/create", string? targetStableKey = "symbol://domain/order", int? maximumDepth = 3, int? limit = null)
        {
            // Stable scope values satisfy common MCP validation and query-layer selector requirements.
            return new ArchonMcpDependencyPathRequest(
                sourceStableKey,
                targetStableKey,
                maximumDepth,
                EdgeKindFilters: ["Calls"],
                limit,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a symbol description request using either stable-key or exact text lookup.
        /// </summary>
        /// <param name="symbolStableKey">The optional symbol stable key.</param>
        /// <param name="searchText">The optional exact symbol search text.</param>
        /// <returns>A symbol description request for MCP handler tests.</returns>
        private static ArchonMcpDescribeSymbolRequest CreateDescribeSymbolRequest(string? symbolStableKey = null, string? searchText = null)
        {
            // Stable repository and solution keys keep symbol lookup bounded to one deterministic snapshot scope.
            return new ArchonMcpDescribeSymbolRequest(
                symbolStableKey,
                searchText,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a symbol usage request for deterministic usage handler tests.
        /// </summary>
        /// <param name="symbolStableKey">The optional symbol stable key.</param>
        /// <param name="usageKindFilters">The optional usage-kind filters.</param>
        /// <param name="projectStableKey">The optional project filter.</param>
        /// <param name="maximumDepth">The optional depth hint retained for MCP request shape.</param>
        /// <param name="limit">The optional usage count limit.</param>
        /// <returns>A symbol usage request for MCP handler tests.</returns>
        private static ArchonMcpFindSymbolUsagesRequest CreateSymbolUsageRequest(string? symbolStableKey = "symbol://orders/create", IReadOnlyList<string>? usageKindFilters = null, string? projectStableKey = null, int? maximumDepth = 1, int? limit = null)
        {
            // Usage lookup uses stable symbol identity and optional filters that are applied after the approved query-layer call.
            return new ArchonMcpFindSymbolUsagesRequest(
                symbolStableKey,
                SearchText: null,
                usageKindFilters,
                projectStableKey,
                maximumDepth,
                limit,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a deterministic dependency path result for path handler tests.
        /// </summary>
        /// <param name="pathFound">A value indicating whether the path result should contain path records.</param>
        /// <param name="unavailable">A value indicating whether query data should be treated as unavailable.</param>
        /// <param name="edgeCount">The number of edges included in the path result.</param>
        /// <returns>A dependency-path query result.</returns>
        private static DependencyPathResult CreatePathResult(bool pathFound, bool unavailable, int edgeCount)
        {
            // Nodes and edges model a simple method-to-domain call chain with stable identities and evidence keys.
            GraphNodeDto source = new("symbol://orders/create", "Method", "OrdersController.Create", "project://src/orders/orders.csproj", ["evidence://symbol/orders-create"], 0.94m, false, null);
            GraphNodeDto service = new("symbol://orders/service", "Method", "OrderService.Create", "project://src/orders/orders.csproj", ["evidence://symbol/orders-service"], 0.91m, false, null);
            GraphNodeDto target = new("symbol://domain/order", "Type", "Order", "project://src/domain/domain.csproj", ["evidence://symbol/domain-order"], 0.9m, false, null);
            GraphEdgeDto[] edges =
            [
                new GraphEdgeDto("edge://orders-create-to-service", "Calls", source.StableKey, service.StableKey, true, ["evidence://path/orders-to-service"], 0.93m, false, null),
                new GraphEdgeDto("edge://orders-service-to-domain", "References", service.StableKey, target.StableKey, true, ["evidence://path/orders-to-domain"], 0.91m, false, null)
            ];
            GraphEdgeDto[] selectedEdges = edges.Take(edgeCount).ToArray();
            GraphNodeDto[] selectedNodes = pathFound ? [source, service, target] : [source, target];
            DependencyPathResponseDto response = new(
                source.StableKey,
                target.StableKey,
                pathFound,
                unavailable,
                pathFound ? null : unavailable ? "Persisted path indexes are unavailable for this snapshot." : "No dependency path was found within the requested depth.",
                4,
                ["Calls", "References"],
                selectedNodes,
                pathFound ? selectedEdges : [],
                new GraphTraversalTruncationDto(false, 100, selectedEdges.Length, null));
            return new DependencyPathResult(response, CreateTraversalContext());
        }

        /// <summary>
        /// Creates a deterministic symbol detail result with source context, evidence, relationships, warnings, and unknowns.
        /// </summary>
        /// <returns>A successful symbol detail result.</returns>
        private static SymbolDetailResult CreateSymbolDetailResult()
        {
            // The snippet intentionally contains a secret-like value so tests verify redaction and untrusted evidence handling.
            SymbolSearchItemDto summary = new(
                "symbol://orders/create",
                "Create",
                "OrdersController.Create",
                "Method",
                "project://src/orders/orders.csproj",
                "Orders.Api",
                "OrdersController",
                "C#",
                new SymbolSourceContextDto("src/orders/OrdersController.cs", 42, 58, "var password = \"SuperSecret\"; return Create(order);"),
                ["evidence://symbol/orders-create"],
                0.94m,
                true,
                "Semantic role could not be completely resolved.");
            SymbolDetailDto detail = new(
                summary,
                [new SymbolEvidenceReferenceDto("evidence://symbol/orders-create", "SymbolDeclaration", "src/orders/OrdersController.cs", 42, 58, "Create", "OrdersController", "hash-create", "token = \"SuperSecret\";", 0.94m)],
                [
                    new SymbolRelationshipDto("edge://orders-create-calls-domain", "Calls", "symbol://orders/create", "symbol://domain/order", ["evidence://path/orders-to-domain"], 0.9m),
                    new SymbolRelationshipDto("rule://symbol/entrypoint", "SymbolRule", "symbol://orders/create", "rule://architecture/entrypoint", ["evidence://symbol/orders-create"], 0.85m)
                ],
                [new SymbolWarningDto("SymbolPartial", "Symbol extraction reported partial semantic data.")],
                [new SymbolUnknownDto("semanticRole", "Symbol role was inferred from partial semantic data.")]);
            return new SymbolDetailResult(detail, CreateSymbolContext());
        }

        /// <summary>
        /// Creates a deterministic symbol usage result with a caller-specified number of usage rows.
        /// </summary>
        /// <param name="usageCount">The number of usage records to include before MCP limiting.</param>
        /// <returns>A successful symbol usage result.</returns>
        private static SymbolUsageResult CreateSymbolUsageResult(int usageCount)
        {
            // Usage rows include untrusted snippets and stable evidence keys for mapping and truncation tests.
            SymbolUsageDto[] usages =
            [
                new SymbolUsageDto("edge://api-calls-orders-create", "Calls", "symbol://api/post", "symbol://orders/create", "OrdersApi.Post", "Create", "src/api/OrdersApi.cs", 20, 22, "SecretToken = \"abc\"; Create(order);", ["evidence://usage/api-orders"], 0.91m, false, null),
                new SymbolUsageDto("edge://worker-calls-orders-create", "Calls", "symbol://worker/run", "symbol://orders/create", "OrdersWorker.Run", "Create", "src/worker/OrdersWorker.cs", 30, 32, "Create(order);", ["evidence://usage/worker-orders"], 0.88m, false, null),
                new SymbolUsageDto("edge://config-references-orders-create", "References", "config://orders", "symbol://orders/create", "OrdersConfig", "Create", "src/orders/appsettings.json", 5, 5, "handler: Create", ["evidence://usage/config-orders"], 0.7m, true, "Configuration binding target could not be proven.")
            ];
            PagedQueryResult<SymbolUsageDto> page = new(usages.Take(usageCount), usageCount, 0, Math.Max(1, usageCount));
            return new SymbolUsageResult(page, CreateSymbolContext());
        }

        /// <summary>
        /// Creates deterministic traversal query context for dependency-path envelopes.
        /// </summary>
        /// <returns>A traversal query context.</returns>
        private static GraphTraversalQueryContext CreateTraversalContext()
        {
            // Context supplies snapshot identity and scope metadata shared by dependency-path envelopes.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new GraphTraversalQueryContext(scope, snapshot, [], []);
        }

        /// <summary>
        /// Creates deterministic symbol query context for symbol envelopes.
        /// </summary>
        /// <returns>A symbol query context.</returns>
        private static SymbolQueryContext CreateSymbolContext()
        {
            // Context supplies snapshot identity and safe query-level diagnostics shared by symbol responses.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new SymbolQueryContext(scope, snapshot, [new SymbolWarningDto("SymbolContext", "Symbol context contains bounded results.")], [new SymbolUnknownDto("symbolCoverage", "Semantic extraction may omit dynamic references.")]);
        }

        /// <summary>
        /// Provides a controllable graph traversal query service for MCP path tests.
        /// </summary>
        private sealed class FakeGraphTraversalQueryService : IGraphTraversalQueryService
        {
            /// <summary>
            /// Stores the dependency-path result returned by fake path queries.
            /// </summary>
            private readonly DependencyPathResult _pathResult;

            /// <summary>
            /// Stores dependency-path queries received by the fake service for assertions.
            /// </summary>
            private readonly List<DependencyPathQuery> _pathQueries = [];

            /// <summary>
            /// Initializes a fake traversal service with a configured path result.
            /// </summary>
            /// <param name="pathResult">The result returned when dependency path search is requested.</param>
            public FakeGraphTraversalQueryService(DependencyPathResult pathResult)
            {
                // Work Item 6 path tests use only dependency-path search; ordinary traversal fails loudly if called unexpectedly.
                _pathResult = pathResult ?? throw new ArgumentNullException(nameof(pathResult));
            }

            /// <summary>
            /// Gets dependency-path queries received by the fake service.
            /// </summary>
            public IReadOnlyList<DependencyPathQuery> PathQueries => _pathQueries;

            /// <inheritdoc />
            public Task<GraphTraversalResult> TraverseAsync(GraphTraversalQuery query, CancellationToken cancellationToken)
            {
                // Ordinary dependency traversal belongs to earlier work items and should not be invoked by path tests.
                throw new NotSupportedException("Dependency traversal is not used by MCP dependency-path tests.");
            }

            /// <inheritdoc />
            public Task<DependencyPathResult> GetDependencyPathAsync(DependencyPathQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves MCP path mapping passes stable identities, depth, and edge filters through the approved seam.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _pathQueries.Add(query);
                return Task.FromResult(_pathResult);
            }
        }

        /// <summary>
        /// Provides a controllable symbol query service for MCP symbol tests.
        /// </summary>
        private sealed class FakeSymbolQueryService : ISymbolQueryService
        {
            /// <summary>
            /// Stores the detail result returned by fake symbol detail queries.
            /// </summary>
            private readonly SymbolDetailResult _detailResult;

            /// <summary>
            /// Stores the usage result returned by fake symbol usage queries.
            /// </summary>
            private readonly SymbolUsageResult _usageResult;

            /// <summary>
            /// Stores detail queries received by the fake service for assertions.
            /// </summary>
            private readonly List<SymbolDetailQuery> _detailQueries = [];

            /// <summary>
            /// Stores usage queries received by the fake service for assertions.
            /// </summary>
            private readonly List<SymbolUsageQuery> _usageQueries = [];

            /// <summary>
            /// Initializes a fake symbol query service with configured detail and usage results.
            /// </summary>
            /// <param name="detailResult">The result returned when symbol detail is requested.</param>
            /// <param name="usageResult">The result returned when symbol usage is requested.</param>
            public FakeSymbolQueryService(SymbolDetailResult detailResult, SymbolUsageResult usageResult)
            {
                // The fake supports only detail and usage operations used by Work Item 6 MCP tests.
                _detailResult = detailResult ?? throw new ArgumentNullException(nameof(detailResult));
                _usageResult = usageResult ?? throw new ArgumentNullException(nameof(usageResult));
            }

            /// <summary>
            /// Gets detail queries received by the fake service.
            /// </summary>
            public IReadOnlyList<SymbolDetailQuery> DetailQueries => _detailQueries;

            /// <summary>
            /// Gets usage queries received by the fake service.
            /// </summary>
            public IReadOnlyList<SymbolUsageQuery> UsageQueries => _usageQueries;

            /// <inheritdoc />
            public Task<SymbolSearchResult> SearchSymbolsAsync(SymbolSearchQuery query, CancellationToken cancellationToken)
            {
                // Search is not expected from these handlers; ambiguity guidance should use safe follow-ups rather than querying here.
                throw new NotSupportedException("Symbol search is not used by MCP symbol tests.");
            }

            /// <inheritdoc />
            public Task<SymbolDetailResult> GetSymbolAsync(SymbolDetailQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves authorization and validation allowed symbol detail lookup.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _detailQueries.Add(query);
                return Task.FromResult(_detailResult);
            }

            /// <inheritdoc />
            public Task<SymbolUsageResult> ListSymbolUsagesAsync(SymbolUsageQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves authorization and validation allowed symbol usage lookup.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _usageQueries.Add(query);
                return Task.FromResult(_usageResult);
            }
        }
    }
}
