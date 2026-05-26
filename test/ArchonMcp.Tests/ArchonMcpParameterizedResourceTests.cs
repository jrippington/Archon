using Archon.Application.Diff;
using Archon.Application.Hotspots;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Symbols;
using Archon.Domain.Graph.Metadata;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpProjects;
using ArchonMcp.McpResources;
using ArchonMcp.McpSnapshotDiff;
using ArchonMcp.McpSymbols;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 Work Item 10 parameterized project, symbol, and snapshot diff MCP resources.
    /// </summary>
    public sealed class ArchonMcpParameterizedResourceTests
    {
        /// <summary>
        /// Confirms a project resource URI returns a bounded project context through the read-only project query seam.
        /// </summary>
        [Fact]
        public async Task ReadProjectResourceReturnsEvidenceBackedProjectContext()
        {
            // The fake project service is the only project data source, proving the resource does not know internal API routes or persistence details.
            FakeProjectQueryService projectService = new(CreateSuccessfulProjectResult(hasUnknownData: true));
            using WebApplication app = BuildParameterizedResourceApp(projectService, new FakeSymbolQueryService(CreateSymbolDetailResult()), new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)));
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://project/project%3A%2F%2Fsrc%2Forders%2Forders.csproj?repository=repository%3A%2F%2Farchon-test&solution=solution%3A%2F%2Farchon-test%2Fmain", CancellationToken.None);

            // The resource envelope uses the common read-resource operation while preserving project facts, evidence, findings, unknowns, and snapshot identity.
            ArchonMcpEnvelope<ArchonMcpProjectFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpProjectFacts>>(payload);
            Assert.Equal(ArchonMcpResourceOperations.ReadResource, envelope.Operation);
            Assert.Equal("project://src/orders/orders.csproj", envelope.Facts.Identity.StableKey);
            Assert.Equal("snapshot://repo/main", envelope.Snapshot?.StableKey);
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://project/orders");
            Assert.Contains(envelope.Findings, finding => finding.StableKey == "finding://hotlist/orders");
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "dataAccess");
            Assert.Contains(envelope.Warnings, warning => warning.Code == "resourceDelegatedToolMapping");
            Assert.Single(projectService.Queries);
            Assert.Equal("project://src/orders/orders.csproj", projectService.Queries[0].ProjectStableKey);
        }

        /// <summary>
        /// Confirms a symbol resource URI returns redacted and evidence-backed symbol context through the read-only symbol query seam.
        /// </summary>
        [Fact]
        public async Task ReadSymbolResourceReturnsRedactedStableKeyOnlySymbolContext()
        {
            // The symbol detail contains secret-like text so the resource must preserve the existing secure evidence mapping behavior.
            FakeSymbolQueryService symbolService = new(CreateSymbolDetailResult());
            using WebApplication app = BuildParameterizedResourceApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), symbolService, new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)));
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://symbol/symbol%3A%2F%2Forders%2Fcreate?repository=repository%3A%2F%2Farchon-test&solution=solution%3A%2F%2Farchon-test%2Fmain", CancellationToken.None);

            // Symbol facts should expose stable symbol and project keys, redacted snippets, untrusted evidence labels, and no raw secret values.
            ArchonMcpEnvelope<ArchonMcpSymbolFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSymbolFacts>>(payload);
            Assert.Equal(ArchonMcpResourceOperations.ReadResource, envelope.Operation);
            Assert.Equal("symbol://orders/create", envelope.Facts.Identity.StableKey);
            Assert.Equal("project://src/orders/orders.csproj", envelope.Facts.ProjectStableKey);
            Assert.Equal("untrusted-repository-evidence", envelope.Facts.Source.TrustLabel);
            Assert.DoesNotContain("SuperSecret", envelope.Facts.Source.SnippetPreview, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SuperSecret", string.Join(' ', envelope.Evidence.Select(evidence => evidence.SnippetPreview)), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(envelope.Facts.Relationships, relationship => relationship.StableKey == "edge://orders-create-calls-domain");
            Assert.Single(symbolService.DetailQueries);
            Assert.Equal("symbol://orders/create", symbolService.DetailQueries[0].SymbolStableKey);
        }

        /// <summary>
        /// Confirms a snapshot diff resource URI returns summary counts, bounded details, fingerprints, evidence, unknowns, and truncation metadata.
        /// </summary>
        [Fact]
        public async Task ReadSnapshotDiffResourceReturnsBoundedSummaryAndDetails()
        {
            // Three diff detail rows with a two-item limit make resource truncation and explicit snapshot query mapping observable.
            FakeSnapshotDiffService diffService = new(CreateDiffResult(changed: true, detailCount: 3));
            using WebApplication app = BuildParameterizedResourceApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), new FakeSymbolQueryService(CreateSymbolDetailResult()), diffService);
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://snapshot/snapshot%3A%2F%2Fcurrent/diff/snapshot%3A%2F%2Fprevious?limit=2&includeDetails=true", CancellationToken.None);

            // The diff resource is an explicit snapshot comparison and returns stable keys and fingerprints rather than persistence internals.
            ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts>>(payload);
            Assert.Equal(ArchonMcpResourceOperations.ReadResource, envelope.Operation);
            Assert.Equal("snapshot://current", envelope.Facts.CurrentSnapshotStableKey);
            Assert.Equal("snapshot://previous", envelope.Facts.PreviousSnapshotStableKey);
            Assert.True(envelope.Facts.HasChanges);
            Assert.Equal(2, envelope.Facts.Details.Count);
            Assert.Contains(envelope.Facts.Details, detail => detail.StableKey == "symbol://legacy/type-0" && detail.CurrentFingerprint == "fingerprint-current-0");
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://diff/0");
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "snapshotDiffDetail");
            Assert.True(envelope.Limits.Truncated);
            Assert.Single(diffService.ExplicitQueries);
            Assert.Equal("snapshot://current", diffService.ExplicitQueries[0].CurrentSnapshotStableKey);
            Assert.Equal("snapshot://previous", diffService.ExplicitQueries[0].PreviousSnapshotStableKey);
        }

        /// <summary>
        /// Confirms malformed parameterized resource URIs fail before delegated query services are invoked.
        /// </summary>
        /// <param name="uri">The malformed or unsupported resource URI to read.</param>
        [Theory]
        [InlineData("archon://project/not-a-stable-key")]
        [InlineData("archon://symbol/project%3A%2F%2Fwrong-kind")]
        [InlineData("archon://snapshot/snapshot%3A%2F%2Fcurrent/delta/snapshot%3A%2F%2Fprevious")]
        [InlineData("archon://snapshot/snapshot%3A%2F%2Fcurrent/diff/not-a-snapshot")]
        [InlineData("archon://project/project%3A%2F%2Fsrc%2Forders%2Forders.csproj?limit=0")]
        [InlineData("archon://snapshot/snapshot%3A%2F%2Fcurrent/diff/snapshot%3A%2F%2Fprevious?includeDetails=yes")]
        public async Task ReadParameterizedResourceRejectsMalformedUrisBeforeQueryExecution(string uri)
        {
            // Captured fake-service calls prove parser validation blocks malformed input before any delegated tool or query seam runs.
            FakeProjectQueryService projectService = new(CreateSuccessfulProjectResult(hasUnknownData: false));
            FakeSymbolQueryService symbolService = new(CreateSymbolDetailResult());
            FakeSnapshotDiffService diffService = new(CreateDiffResult(changed: true, detailCount: 1));
            using WebApplication app = BuildParameterizedResourceApp(projectService, symbolService, diffService);
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync(uri, CancellationToken.None);

            // All malformed parameterized resources should return safe structured errors with no downstream calls.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.True(error.Error.Category is ArchonMcpErrorCategory.Validation or ArchonMcpErrorCategory.UnsupportedOperation);
            Assert.Empty(projectService.Queries);
            Assert.Empty(symbolService.DetailQueries);
            Assert.Empty(diffService.ExplicitQueries);
        }

        /// <summary>
        /// Confirms disabled resource reads fail authorization before URI parsing or delegated query execution.
        /// </summary>
        [Fact]
        public async Task DisabledParameterizedResourceReadReturnsForbiddenBeforeDelegatedQueries()
        {
            // The allow-list omits archon.read_resource so the dispatcher must fail before parser-supported path details are observable.
            FakeProjectQueryService projectService = new(CreateSuccessfulProjectResult(hasUnknownData: false));
            FakeSymbolQueryService symbolService = new(CreateSymbolDetailResult());
            FakeSnapshotDiffService diffService = new(CreateDiffResult(changed: true, detailCount: 1));
            using WebApplication app = BuildParameterizedResourceApp(projectService, symbolService, diffService, allowedOperations: ["archon.health", ArchonMcpProjectOperation.Name, ArchonMcpSymbolOperations.DescribeSymbol, ArchonMcpSnapshotDiffOperations.GetSnapshotDiff]);
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://project/project%3A%2F%2Fsrc%2Forders%2Forders.csproj?repository=repository%3A%2F%2Farchon-test", CancellationToken.None);

            // Forbidden output and empty query captures prove resource authorization is the first gate.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(projectService.Queries);
            Assert.Empty(symbolService.DetailQueries);
            Assert.Empty(diffService.ExplicitQueries);
        }

        /// <summary>
        /// Confirms delegated not-found failures are preserved as safe MCP resource errors.
        /// </summary>
        [Fact]
        public async Task ReadParameterizedResourcePreservesDelegatedNotFoundErrors()
        {
            // A query-layer project-not-found signal should surface as a resource read error rather than an empty success payload.
            ProjectDetailResult notFound = new([new ProjectQueryValidationError(ProjectQueryValidationCodes.ProjectNotFound, "The requested project was not found.")]);
            FakeProjectQueryService projectService = new(notFound);
            using WebApplication app = BuildParameterizedResourceApp(projectService, new FakeSymbolQueryService(CreateSymbolDetailResult()), new FakeSnapshotDiffService(CreateDiffResult(changed: true, detailCount: 1)));
            IArchonMcpResourceDispatcher dispatcher = app.Services.GetRequiredService<IArchonMcpResourceDispatcher>();

            object payload = await dispatcher.ReadResourceAsync("archon://project/project%3A%2F%2Fmissing%2Fmissing.csproj?repository=repository%3A%2F%2Farchon-test", CancellationToken.None);

            // The not-found category tells clients the stable resource identity was well-formed but no matching data was available.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.NotFound, error.Error.Category);
            Assert.Single(projectService.Queries);
        }

        /// <summary>
        /// Builds an MCP host application with fake project, symbol, diff, and current-resource query services plus configurable security.
        /// </summary>
        /// <param name="projectService">The fake project query service used by project resources.</param>
        /// <param name="symbolService">The fake symbol query service used by symbol resources.</param>
        /// <param name="snapshotDiffService">The fake snapshot diff service used by diff resources.</param>
        /// <param name="allowedOperations">The optional operation allow-list for security tests.</param>
        /// <returns>A configured web application exposing Work Item 10 resource services.</returns>
        private static WebApplication BuildParameterizedResourceApp(FakeProjectQueryService projectService, FakeSymbolQueryService symbolService, FakeSnapshotDiffService snapshotDiffService, string[]? allowedOperations = null)
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
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpResourceOperations.ReadResource, ArchonMcpProjectOperation.Name, ArchonMcpSymbolOperations.DescribeSymbol, ArchonMcpSnapshotDiffOperations.GetSnapshotDiff];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder =>
            {
                // Parameterized resources delegate to tool handlers, so these fakes satisfy the underlying application/query seams.
                builder.Services.AddSingleton<IProjectQueryService>(projectService);
                builder.Services.AddSingleton<ISymbolQueryService>(symbolService);
                builder.Services.AddSingleton<ISnapshotDiffService>(snapshotDiffService);
                builder.Services.AddSingleton<IArchonMcpCurrentSnapshotProvider>(new FakeCurrentSnapshotProvider());
                builder.Services.AddSingleton<IHotlistQueryService>(new FakeHotlistQueryService());
                builder.Services.AddSingleton<IHotspotQueryService>(new FakeHotspotQueryService());
            });
        }

        /// <summary>
        /// Creates a successful project detail result with deterministic project facts.
        /// </summary>
        /// <param name="hasUnknownData">A value indicating whether unknown project data should be included.</param>
        /// <returns>A successful project detail result.</returns>
        private static ProjectDetailResult CreateSuccessfulProjectResult(bool hasUnknownData)
        {
            // The DTO mirrors application/query output and includes project sections that resource mapping must preserve.
            ProjectCatalogueItemDto summary = new(
                "project://src/orders/orders.csproj",
                "Orders",
                "src/orders/orders.csproj",
                "C#",
                "Application",
                "net10.0",
                isSdkStyle: true,
                dependencyCount: 2,
                dependentCount: 1,
                packageCount: 1,
                endpointCount: 1,
                ["EntityFrameworkCore"],
                hotlistCount: 1,
                new ProjectRiskIndicatorsDto(true, 1, "High", hasUnknownData, hasUnknownData ? "Project has incomplete extracted data." : null),
                ["evidence://project/orders"],
                0.92m,
                hasUnknownData,
                hasUnknownData ? "Project has incomplete extracted data." : null);
            ProjectDetailDto detail = new(
                summary,
                [new ResponsibilitySummaryDto("Order processing", "Coordinates order processing workflow.", ["evidence://responsibility/orders"])],
                [new EvidenceReferenceDto("evidence://project/orders", "ProjectFile", "src/orders/orders.csproj", 1, 20, "Orders", "hash-orders")],
                ["Orders.Program"],
                ["project://src/domain/domain.csproj"],
                ["project://src/api/api.csproj"],
                ["package://nuget/newtonsoft.json"],
                "Worker",
                ["endpoint://orders/create"],
                ["OrdersHostedService"],
                ["DbContext:OrdersDbContext"],
                ["ConnectionStrings:Orders"],
                ["ServiceBus:orders"],
                ["finding://hotlist/orders"],
                new ScopedGraphSummaryDto(8, 2, 1, 1, 1, 1),
                hasUnknownData ? [new ProjectUnknownDto("dataAccess", "Dynamic SQL target could not be resolved.")] : [],
                [new ProjectWarningDto("SnapshotWarning", "Snapshot contained non-blocking diagnostics.")],
                GraphMetadata.From(new Dictionary<string, object?> { ["architecture.layer"] = "Application" }));
            return new ProjectDetailResult(detail, CreateProjectContext(hasUnknownData));
        }

        /// <summary>
        /// Creates deterministic project query context for resource envelopes.
        /// </summary>
        /// <param name="hasUnknownData">A value indicating whether query-level unknowns should be included.</param>
        /// <returns>A project query context.</returns>
        private static ProjectQueryContext CreateProjectContext(bool hasUnknownData)
        {
            // Context supplies snapshot identity and optional unknowns shared by the MCP project mapper.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new ProjectQueryContext(scope, snapshot, [], hasUnknownData ? [new ProjectUnknownDto("snapshotDiagnostics", "Snapshot has incomplete project sections.")] : []);
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
        /// Creates a deterministic successful snapshot diff result for resource tests.
        /// </summary>
        /// <param name="changed">Indicates whether the summary should include changes.</param>
        /// <param name="detailCount">The number of detail rows to include before MCP limiting.</param>
        /// <returns>A successful snapshot diff result.</returns>
        private static SnapshotDiffResult CreateDiffResult(bool changed, int detailCount)
        {
            // Summaries and details use stable keys and fingerprints so resources can prove no raw graph identifiers are required.
            SnapshotDiffSummaryDto[] summaries = changed
                ? [new SnapshotDiffSummaryDto("Nodes", AddedCount: 1, RemovedCount: 0, ChangedCount: 2, UnchangedCount: 5)]
                : [new SnapshotDiffSummaryDto("Nodes", AddedCount: 0, RemovedCount: 0, ChangedCount: 0, UnchangedCount: 7)];
            SnapshotDiffItemDto[] details = Enumerable.Range(0, detailCount)
                .Select(index => new SnapshotDiffItemDto(
                    "Nodes",
                    index == 0 ? "Changed" : "Added",
                    $"symbol://legacy/type-{index}",
                    $"LegacyType{index}",
                    "Symbol",
                    "project://legacy/app",
                    [$"project://legacy/app"],
                    Severity: null,
                    PreviousFingerprint: index == 0 ? $"fingerprint-previous-{index}" : null,
                    CurrentFingerprint: $"fingerprint-current-{index}",
                    ChangedFields: ["relationships"],
                    EvidenceStableKeys: [$"evidence://diff/{index}"],
                    HasUnknownData: index == 0,
                    UnknownReason: index == 0 ? "One changed field could not be fully classified." : null))
                .ToArray();
            SnapshotDiffTruncationDto truncation = new(detailCount > 2, detailCount, Math.Min(detailCount, 2), Skip: 0, Take: Math.Max(1, detailCount));
            return new SnapshotDiffResult("snapshot://current", "snapshot://previous", "repository://archon-test", summaries, details, truncation);
        }

        /// <summary>
        /// Provides a controllable project query service for parameterized resource tests.
        /// </summary>
        private sealed class FakeProjectQueryService : IProjectQueryService
        {
            /// <summary>
            /// Stores the detail result returned by fake project detail queries.
            /// </summary>
            private readonly ProjectDetailResult _detailResult;

            /// <summary>
            /// Creates a fake project query service with a configured detail result.
            /// </summary>
            /// <param name="detailResult">The result returned when project detail is requested.</param>
            public FakeProjectQueryService(ProjectDetailResult detailResult)
            {
                // Captured queries let tests prove resource parsing, authorization, and tool delegation behavior.
                _detailResult = detailResult ?? throw new ArgumentNullException(nameof(detailResult));
            }

            /// <summary>
            /// Gets captured project detail queries.
            /// </summary>
            public List<ProjectDetailQuery> Queries { get; } = [];

            /// <inheritdoc />
            public Task<ProjectCatalogueResult> ListProjectsAsync(ProjectCatalogueQuery query, CancellationToken cancellationToken)
            {
                // Parameterized project resources describe exact stable keys and never list the project catalogue.
                throw new NotSupportedException("Project catalogue listing is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<ProjectDetailResult> GetProjectAsync(ProjectDetailQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves validation and authorization allowed the delegated tool query to run.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                Queries.Add(query);
                return Task.FromResult(_detailResult);
            }
        }

        /// <summary>
        /// Provides a controllable symbol query service for parameterized resource tests.
        /// </summary>
        private sealed class FakeSymbolQueryService : ISymbolQueryService
        {
            /// <summary>
            /// Stores the detail result returned by fake symbol detail queries.
            /// </summary>
            private readonly SymbolDetailResult _detailResult;

            /// <summary>
            /// Creates a fake symbol query service with a configured detail result.
            /// </summary>
            /// <param name="detailResult">The result returned when symbol detail is requested.</param>
            public FakeSymbolQueryService(SymbolDetailResult detailResult)
            {
                // Captured queries let tests prove stable-key-only symbol resource lookup.
                _detailResult = detailResult ?? throw new ArgumentNullException(nameof(detailResult));
            }

            /// <summary>
            /// Gets captured symbol detail queries.
            /// </summary>
            public List<SymbolDetailQuery> DetailQueries { get; } = [];

            /// <inheritdoc />
            public Task<SymbolSearchResult> SearchSymbolsAsync(SymbolSearchQuery query, CancellationToken cancellationToken)
            {
                // Symbol resources are stable-key-only and must not perform search-text disambiguation.
                throw new NotSupportedException("Symbol search is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<SymbolDetailResult> GetSymbolAsync(SymbolDetailQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves validation and authorization allowed symbol detail lookup.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                DetailQueries.Add(query);
                return Task.FromResult(_detailResult);
            }

            /// <inheritdoc />
            public Task<SymbolUsageResult> ListSymbolUsagesAsync(SymbolUsageQuery query, CancellationToken cancellationToken)
            {
                // Symbol resources do not expand usage lists; clients can use the related read-only tool for that workflow.
                throw new NotSupportedException("Symbol usage listing is not used by parameterized resource tests.");
            }
        }

        /// <summary>
        /// Provides deterministic snapshot diff behavior for parameterized resource tests.
        /// </summary>
        private sealed class FakeSnapshotDiffService : ISnapshotDiffService
        {
            /// <summary>
            /// Stores the diff result returned by explicit comparison queries.
            /// </summary>
            private readonly SnapshotDiffResult _result;

            /// <summary>
            /// Creates a fake snapshot diff service with deterministic behavior.
            /// </summary>
            /// <param name="result">The result returned by compare operations.</param>
            public FakeSnapshotDiffService(SnapshotDiffResult result)
            {
                // Captured query lists let tests prove validation and authorization stop calls when expected.
                _result = result ?? throw new ArgumentNullException(nameof(result));
            }

            /// <summary>
            /// Gets captured explicit snapshot diff queries.
            /// </summary>
            public List<SnapshotDiffQuery> ExplicitQueries { get; } = [];

            /// <inheritdoc />
            public Task<SnapshotDiffResult> CompareSnapshotsAsync(SnapshotDiffQuery query, CancellationToken cancellationToken)
            {
                // Explicit diff resources should call only the explicit comparison seam.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                ExplicitQueries.Add(query);
                return Task.FromResult(_result);
            }

            /// <inheritdoc />
            public Task<SnapshotDiffResult> CompareLatestToPreviousAsync(SnapshotDiffLatestQuery query, CancellationToken cancellationToken)
            {
                // Parameterized diff resources require explicit snapshot keys and must not infer latest-to-previous comparisons.
                throw new NotSupportedException("Latest-to-previous snapshot diff is not used by parameterized resource tests.");
            }
        }

        /// <summary>
        /// Provides an unused current snapshot provider required by the shared resource runtime composition.
        /// </summary>
        private sealed class FakeCurrentSnapshotProvider : IArchonMcpCurrentSnapshotProvider
        {
            /// <inheritdoc />
            public Task<ArchonMcpCurrentSnapshotResolution> ResolveCurrentSnapshotAsync(ArchonMcpCurrentSnapshotRequest request, CancellationToken cancellationToken)
            {
                // Parameterized resources do not resolve current snapshot context, so this fake fails if routing regresses.
                throw new NotSupportedException("Current snapshot resolution is not used by parameterized resource tests.");
            }
        }

        /// <summary>
        /// Provides an unused hotlist query service required by the shared resource runtime composition.
        /// </summary>
        private sealed class FakeHotlistQueryService : IHotlistQueryService
        {
            /// <inheritdoc />
            public Task<PagedQueryResult<RuleCatalogItemDto>> ListRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken)
            {
                // Current rules resources are outside the parameterized resource tests.
                throw new NotSupportedException("Rule listing is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<RuleDetailDto?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken)
            {
                // Rule detail is outside the parameterized resource tests.
                throw new NotSupportedException("Rule detail is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<PagedQueryResult<HotlistItemDto>> ListHotlistAsync(HotlistQuery query, CancellationToken cancellationToken)
            {
                // Current hotlist resources are outside the parameterized resource tests.
                throw new NotSupportedException("Hotlist listing is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<FindingDetailDto?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken)
            {
                // Finding detail is outside the parameterized resource tests.
                throw new NotSupportedException("Finding detail is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<FindingHistoryDto?> GetFindingHistoryAsync(string historyKey, CancellationToken cancellationToken)
            {
                // Finding history is outside the parameterized resource tests.
                throw new NotSupportedException("Finding history is not used by parameterized resource tests.");
            }

            /// <inheritdoc />
            public Task<SuppressionCommandResult> SuppressFindingAsync(SuppressFindingCommand command, CancellationToken cancellationToken)
            {
                // Suppression is deliberately unsupported by MCP resources.
                throw new NotSupportedException("Finding suppression is not used by parameterized resource tests.");
            }
        }

        /// <summary>
        /// Provides an unused hotspot query service required by the shared resource runtime composition.
        /// </summary>
        private sealed class FakeHotspotQueryService : IHotspotQueryService
        {
            /// <inheritdoc />
            public Task<PagedQueryResult<HotspotItemDto>> ListHotspotsAsync(HotspotQuery query, CancellationToken cancellationToken)
            {
                // Current hotspots resources are outside the parameterized resource tests.
                throw new NotSupportedException("Hotspot listing is not used by parameterized resource tests.");
            }
        }
    }
}
