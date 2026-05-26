using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Traversal;
using Archon.Domain.Graph.Metadata;
using ArchonMcp.McpDependencies;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpProjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 project description and dependency traversal MCP tools across success, validation, security, failure, ambiguity, and truncation paths.
    /// </summary>
    public sealed class ArchonMcpProjectAndDependencyTests
    {
        /// <summary>
        /// Confirms project description returns project identity, runtime facts, graph facts, evidence, findings, unknowns, and safe follow-ups.
        /// </summary>
        [Fact]
        public async Task DescribeProjectReturnsEvidenceBackedProjectFacts()
        {
            // The fake project query service is the approved application/query seam and must be the only source of project facts.
            ProjectDetailResult result = CreateSuccessfulProjectResult(hasUnknownData: true);
            FakeProjectQueryService projectService = new(result);
            using WebApplication app = BuildProjectDependencyApp(projectService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1)));
            IArchonMcpProjectTool tool = app.Services.GetRequiredService<IArchonMcpProjectTool>();

            object payload = await tool.DescribeProjectAsync(CreateProjectRequest(projectStableKey: "project://src/orders/orders.csproj"), CancellationToken.None);

            // The envelope proves project detail is mapped into stable MCP facts without exposing raw persistence identifiers.
            ArchonMcpEnvelope<ArchonMcpProjectFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpProjectFacts>>(payload);
            Assert.Equal(ArchonMcpProjectOperation.Name, envelope.Operation);
            Assert.Equal("snapshot://repo/main", envelope.Snapshot?.StableKey);
            Assert.Equal("project://src/orders/orders.csproj", envelope.Facts.Identity.StableKey);
            Assert.Equal("SdkStyle", envelope.Facts.Identity.ProjectFormat);
            Assert.Equal("Worker", envelope.Facts.Identity.ApplicationType);
            Assert.Equal(2, envelope.Facts.Graph.OutgoingDependencyCount);
            Assert.Contains("package://nuget/newtonsoft.json", envelope.Facts.Graph.Packages);
            Assert.Contains("OrdersHostedService", envelope.Facts.Runtime.Workers);
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://project/orders");
            Assert.Contains(envelope.Findings, finding => finding.StableKey == "finding://hotlist/orders");
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "dataAccess");
            Assert.Contains(envelope.SuggestedFollowUps, followUp => followUp.Operation == ArchonMcpDependencyOperations.GetDependencies);
            Assert.Single(projectService.Queries);
        }

        /// <summary>
        /// Confirms ambiguous project-name lookup returns an MCP ambiguity error with stable disambiguation candidates.
        /// </summary>
        [Fact]
        public async Task DescribeProjectReturnsAmbiguityErrorForAmbiguousName()
        {
            // Ambiguity must fail safely rather than selecting one project arbitrarily.
            FakeProjectQueryService projectService = new(CreateAmbiguousProjectResult());
            using WebApplication app = BuildProjectDependencyApp(projectService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1)));
            IArchonMcpProjectTool tool = app.Services.GetRequiredService<IArchonMcpProjectTool>();

            object payload = await tool.DescribeProjectAsync(CreateProjectRequest(projectName: "Orders"), CancellationToken.None);

            // The structured error category lets MCP clients ask the user or retry with an exact stable key.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Ambiguous, error.Error.Category);
            Assert.Contains("project://src/orders/orders.csproj", error.SuggestedFollowUps[0].Parameters?["candidates"]);
        }

        /// <summary>
        /// Confirms project description validation failures stop before the project query service is invoked.
        /// </summary>
        [Fact]
        public async Task DescribeProjectValidationFailureDoesNotInvokeQueryLayer()
        {
            // Missing project identity is rejected at the MCP boundary before query-layer work starts.
            FakeProjectQueryService projectService = new(CreateSuccessfulProjectResult(hasUnknownData: false));
            using WebApplication app = BuildProjectDependencyApp(projectService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1)));
            IArchonMcpProjectTool tool = app.Services.GetRequiredService<IArchonMcpProjectTool>();

            object payload = await tool.DescribeProjectAsync(CreateProjectRequest(), CancellationToken.None);

            // A validation error and zero queries prove malformed input does not reach the application/query layer.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(projectService.Queries);
        }

        /// <summary>
        /// Confirms disabled project description requests are forbidden before validation or query-layer execution occurs.
        /// </summary>
        [Fact]
        public async Task DisabledDescribeProjectReturnsForbiddenBeforeQueryLayerIsInvoked()
        {
            // The configured allow-list omits archon.describe_project so the operation executor must fail closed first.
            FakeProjectQueryService projectService = new(CreateSuccessfulProjectResult(hasUnknownData: false));
            using WebApplication app = BuildProjectDependencyApp(projectService, new FakeGraphTraversalQueryService(CreateTraversalResult(edgeCount: 1)), allowedOperations: ["archon.health"]);
            IArchonMcpProjectTool tool = app.Services.GetRequiredService<IArchonMcpProjectTool>();

            object payload = await tool.DescribeProjectAsync(CreateProjectRequest(projectStableKey: "project://src/orders/orders.csproj"), CancellationToken.None);

            // Forbidden output and zero queries prove authorization precedes project validation and mapping.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(projectService.Queries);
        }

        /// <summary>
        /// Confirms outgoing dependency traversal returns deterministic relationship facts, evidence references, limits, and follow-ups.
        /// </summary>
        [Fact]
        public async Task GetDependenciesReturnsDirectTraversalFacts()
        {
            // The fake traversal service captures the query so the test can verify outgoing direct traversal mapping.
            FakeGraphTraversalQueryService traversalService = new(CreateTraversalResult(edgeCount: 1));
            using WebApplication app = BuildProjectDependencyApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), traversalService);
            IArchonMcpDependencyTool tool = app.Services.GetRequiredService<IArchonMcpDependencyTool>();

            object payload = await tool.GetDependenciesAsync(CreateTraversalRequest(nodeStableKey: "project://src/orders/orders.csproj"), CancellationToken.None);

            // The envelope contains stable nodes and relationships only, with evidence references but no graph database internals.
            ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts>>(payload);
            Assert.Equal(ArchonMcpDependencyOperations.GetDependencies, envelope.Operation);
            Assert.True(envelope.Facts.DirectOnly);
            Assert.Equal("Outgoing", envelope.Facts.Direction);
            Assert.Single(envelope.Facts.Relationships);
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://edge/orders-to-domain");
            Assert.Contains(envelope.SuggestedFollowUps, followUp => followUp.Operation == ArchonMcpDependencyOperations.GetDependents);
            Assert.Single(traversalService.Queries);
            Assert.Equal("Outgoing", traversalService.Queries[0].Direction);
            Assert.Equal(1, traversalService.Queries[0].Depth);
        }

        /// <summary>
        /// Confirms incoming dependent traversal supports transitive mode, configured depth, deterministic ordering, and truncation metadata.
        /// </summary>
        [Fact]
        public async Task GetDependentsReturnsTransitiveTraversalWithTruncation()
        {
            // Three relationships with a two-item MCP limit should report truncation and preserve incoming traversal direction.
            FakeGraphTraversalQueryService traversalService = new(CreateTraversalResult(edgeCount: 3));
            using WebApplication app = BuildProjectDependencyApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), traversalService);
            IArchonMcpDependencyTool tool = app.Services.GetRequiredService<IArchonMcpDependencyTool>();

            object payload = await tool.GetDependentsAsync(CreateTraversalRequest(nodeStableKey: "project://src/orders/orders.csproj", transitive: true, maximumDepth: 3, limit: 2), CancellationToken.None);

            // Truncation metadata prevents clients from treating the returned edge subset as complete dependent knowledge.
            ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts>>(payload);
            Assert.Equal(ArchonMcpDependencyOperations.GetDependents, envelope.Operation);
            Assert.False(envelope.Facts.DirectOnly);
            Assert.Equal("Incoming", envelope.Facts.Direction);
            Assert.Equal(3, envelope.Facts.MaximumDepth);
            Assert.Equal(2, envelope.Facts.Relationships.Count);
            Assert.True(envelope.Limits.Truncated);
            Assert.Contains(envelope.Warnings, warning => warning.Code == "mcp.archon.get_dependents.truncated");
            Assert.Equal("Incoming", traversalService.Queries[0].Direction);
            Assert.Equal(3, traversalService.Queries[0].Depth);
        }

        /// <summary>
        /// Confirms successful empty traversal is represented as known absence rather than unavailable graph data.
        /// </summary>
        [Fact]
        public async Task GetDependenciesDistinguishesNoDependenciesFromUnavailableData()
        {
            // A successful traversal with zero edges means data was available but no matching dependency relationships were found.
            FakeGraphTraversalQueryService traversalService = new(CreateTraversalResult(edgeCount: 0));
            using WebApplication app = BuildProjectDependencyApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), traversalService);
            IArchonMcpDependencyTool tool = app.Services.GetRequiredService<IArchonMcpDependencyTool>();

            object payload = await tool.GetDependenciesAsync(CreateTraversalRequest(nodeStableKey: "project://src/orders/orders.csproj"), CancellationToken.None);

            // The success envelope carries noDependencies unknown semantics while unavailable scope would be a structured error.
            ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts>>(payload);
            Assert.Empty(envelope.Facts.Relationships);
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "noDependencies");
            Assert.Contains(envelope.Warnings, warning => warning.Code == "mcp.archon.get_dependencies.empty");
        }

        /// <summary>
        /// Confirms dependency traversal validation failures stop before the traversal query service is invoked.
        /// </summary>
        [Fact]
        public async Task GetDependenciesValidationFailureDoesNotInvokeQueryLayer()
        {
            // Project-name traversal is rejected in this slice because the traversal query layer accepts stable node identities.
            FakeGraphTraversalQueryService traversalService = new(CreateTraversalResult(edgeCount: 1));
            using WebApplication app = BuildProjectDependencyApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), traversalService);
            IArchonMcpDependencyTool tool = app.Services.GetRequiredService<IArchonMcpDependencyTool>();

            object payload = await tool.GetDependenciesAsync(CreateTraversalRequest(projectName: "Orders"), CancellationToken.None);

            // A validation error and zero traversal queries prove invalid identity input stays at the MCP boundary.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Empty(traversalService.Queries);
        }

        /// <summary>
        /// Confirms traversal query-layer scope failures are mapped to dependency-unavailable MCP errors.
        /// </summary>
        [Fact]
        public async Task GetDependentsUnavailableDataReturnsDependencyUnavailableError()
        {
            // Repository-not-found is the application/query signal that traversal data is unavailable for the supplied scope.
            FakeGraphTraversalQueryService traversalService = new(new GraphTraversalResult([
                new GraphTraversalValidationError(GraphTraversalValidationCodes.RepositoryNotFound, "The requested repository scope was not found.")]
            ));
            using WebApplication app = BuildProjectDependencyApp(new FakeProjectQueryService(CreateSuccessfulProjectResult(hasUnknownData: false)), traversalService);
            IArchonMcpDependencyTool tool = app.Services.GetRequiredService<IArchonMcpDependencyTool>();

            object payload = await tool.GetDependentsAsync(CreateTraversalRequest(nodeStableKey: "project://src/orders/orders.csproj"), CancellationToken.None);

            // Unavailable graph data is a dependency-unavailable error, distinct from an empty successful traversal.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.DependencyUnavailable, error.Error.Category);
            Assert.Contains("unavailable", error.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds an MCP host application with fake project and traversal query services plus security configuration.
        /// </summary>
        /// <param name="projectService">The fake project query service to register for the test.</param>
        /// <param name="traversalService">The fake traversal query service to register for the test.</param>
        /// <param name="allowedOperations">The optional operation allow-list for security-path tests.</param>
        /// <param name="callerId">The test caller identifier used by the default caller-context provider.</param>
        /// <returns>A configured web application that exposes MCP project and dependency services.</returns>
        private static WebApplication BuildProjectDependencyApp(FakeProjectQueryService projectService, FakeGraphTraversalQueryService traversalService, string[]? allowedOperations = null, string? callerId = "developer-1")
        {
            // Tests use production host composition and replace only the application/query seams under test.
            List<string> args =
            [
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                $"Archon:Mcp:Security:TestCallerId={callerId}",
                "Archon:Mcp:Limits:MaxResultCount=2"
            ];
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpProjectOperation.Name, ArchonMcpDependencyOperations.GetDependencies, ArchonMcpDependencyOperations.GetDependents];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder =>
            {
                // Replacing the query seams keeps tests deterministic and proves MCP tools do not bypass application abstractions.
                builder.Services.AddSingleton<IProjectQueryService>(projectService);
                builder.Services.AddSingleton<IGraphTraversalQueryService>(traversalService);
            });
        }

        /// <summary>
        /// Creates a valid project description request unless identity parameters are intentionally omitted for validation tests.
        /// </summary>
        /// <param name="projectStableKey">The optional project stable key.</param>
        /// <param name="projectName">The optional project display name.</param>
        /// <returns>A project description request scoped to the test repository and snapshot.</returns>
        private static ArchonMcpDescribeProjectRequest CreateProjectRequest(string? projectStableKey = null, string? projectName = null)
        {
            // Stable repository and solution keys satisfy common MCP validation and query-layer selector requirements.
            return new ArchonMcpDescribeProjectRequest(
                projectStableKey,
                projectName,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a dependency traversal request for test scenarios.
        /// </summary>
        /// <param name="nodeStableKey">The optional graph node stable key.</param>
        /// <param name="projectName">The optional project name used in validation tests.</param>
        /// <param name="transitive">The optional transitive-mode flag.</param>
        /// <param name="maximumDepth">The optional maximum traversal depth.</param>
        /// <param name="limit">The optional returned relationship limit.</param>
        /// <returns>A dependency traversal request scoped to the test repository and snapshot.</returns>
        private static ArchonMcpDependencyTraversalRequest CreateTraversalRequest(string? nodeStableKey = null, string? projectName = null, bool? transitive = null, int? maximumDepth = null, int? limit = null)
        {
            // Stable repository and solution keys satisfy common MCP validation and query-layer selector requirements.
            return new ArchonMcpDependencyTraversalRequest(
                nodeStableKey,
                ProjectStableKey: null,
                projectName,
                transitive,
                maximumDepth,
                EdgeKindFilters: null,
                limit,
                "latest",
                "repository://archon-test",
                "solution://archon-test/main");
        }

        /// <summary>
        /// Creates a successful project detail result with deterministic project facts.
        /// </summary>
        /// <param name="hasUnknownData">A value indicating whether unknown project data should be included.</param>
        /// <returns>A successful project detail result.</returns>
        private static ProjectDetailResult CreateSuccessfulProjectResult(bool hasUnknownData)
        {
            // The DTO mirrors application/query output and includes every project section that MCP mapping must preserve.
            ProjectCatalogueItemDto summary = CreateProjectCatalogueItem("project://src/orders/orders.csproj", "Orders", hasUnknownData);
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
        /// Creates an ambiguous project detail result with safe stable-key candidates.
        /// </summary>
        /// <returns>A failed project detail result containing disambiguation options.</returns>
        private static ProjectDetailResult CreateAmbiguousProjectResult()
        {
            // Candidate rows are safe to return because they contain public stable keys and display metadata only.
            ProjectQueryValidationError error = new(ProjectQueryValidationCodes.ProjectNameAmbiguous, "The requested project name matches multiple projects.");
            return new ProjectDetailResult([
                error],
                [
                    CreateProjectCatalogueItem("project://src/orders/orders.csproj", "Orders", hasUnknownData: false),
                    CreateProjectCatalogueItem("project://test/orders.tests/orders.tests.csproj", "Orders", hasUnknownData: false)
                ]);
        }

        /// <summary>
        /// Creates one deterministic project catalogue item used by project detail test DTOs.
        /// </summary>
        /// <param name="stableKey">The stable project key.</param>
        /// <param name="name">The project display name.</param>
        /// <param name="hasUnknownData">A value indicating whether unknown state should be set on the row.</param>
        /// <returns>A project catalogue item DTO.</returns>
        private static ProjectCatalogueItemDto CreateProjectCatalogueItem(string stableKey, string name, bool hasUnknownData)
        {
            // Counts and risk fields are intentionally non-zero so mapping assertions can prove section preservation.
            return new ProjectCatalogueItemDto(
                stableKey,
                name,
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
        }

        /// <summary>
        /// Creates deterministic project query context for test envelopes.
        /// </summary>
        /// <param name="hasUnknownData">A value indicating whether query-level unknowns should be included.</param>
        /// <returns>A project query context.</returns>
        private static ProjectQueryContext CreateProjectContext(bool hasUnknownData)
        {
            // Context supplies snapshot identity and optional unknowns shared by the MCP envelope mapper.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new ProjectQueryContext(scope, snapshot, [], hasUnknownData ? [new ProjectUnknownDto("snapshotDiagnostics", "Snapshot has incomplete project sections.")] : []);
        }

        /// <summary>
        /// Creates a successful graph traversal result with a deterministic number of relationship records.
        /// </summary>
        /// <param name="edgeCount">The number of edge records to include in the response.</param>
        /// <returns>A successful traversal result.</returns>
        private static GraphTraversalResult CreateTraversalResult(int edgeCount)
        {
            // The first edge represents a normal dependency; additional edges support truncation tests.
            GraphNodeDto start = new("project://src/orders/orders.csproj", "Project", "Orders", "project://src/orders/orders.csproj", ["evidence://project/orders"], 0.95m, false, null);
            GraphNodeDto domain = new("project://src/domain/domain.csproj", "Project", "Domain", "project://src/domain/domain.csproj", ["evidence://project/domain"], 0.9m, false, null);
            GraphNodeDto api = new("project://src/api/api.csproj", "Project", "Api", "project://src/api/api.csproj", ["evidence://project/api"], 0.88m, false, null);
            GraphNodeDto worker = new("project://src/worker/worker.csproj", "Project", "Worker", "project://src/worker/worker.csproj", ["evidence://project/worker"], 0.87m, false, null);
            GraphEdgeDto[] allEdges =
            [
                new GraphEdgeDto("edge://orders-to-domain", "References", start.StableKey, domain.StableKey, true, ["evidence://edge/orders-to-domain"], 0.93m, false, null),
                new GraphEdgeDto("edge://api-to-orders", "References", api.StableKey, start.StableKey, true, ["evidence://edge/api-to-orders"], 0.89m, false, null),
                new GraphEdgeDto("edge://worker-to-orders", "References", worker.StableKey, start.StableKey, true, ["evidence://edge/worker-to-orders"], 0.86m, false, null)
            ];
            GraphEdgeDto[] edges = allEdges.Take(edgeCount).ToArray();
            GraphTraversalResponseDto response = new(
                start.StableKey,
                "DirectDependencies",
                "Outgoing",
                1,
                ["References"],
                [start, domain, api, worker],
                edges,
                new GraphTraversalTruncationDto(false, 100, edges.Length, null));
            return new GraphTraversalResult(response, CreateTraversalContext());
        }

        /// <summary>
        /// Creates deterministic graph traversal context for test envelopes.
        /// </summary>
        /// <returns>A traversal query context.</returns>
        private static GraphTraversalQueryContext CreateTraversalContext()
        {
            // Context supplies snapshot identity and scope metadata shared by dependency and dependent envelopes.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            return new GraphTraversalQueryContext(scope, snapshot, [], []);
        }

        /// <summary>
        /// Provides a controllable project query service for MCP project tests.
        /// </summary>
        private sealed class FakeProjectQueryService : IProjectQueryService
        {
            /// <summary>
            /// Stores the detail result returned by fake project detail queries.
            /// </summary>
            private readonly ProjectDetailResult _detailResult;

            /// <summary>
            /// Stores project detail queries received by the fake service for assertions.
            /// </summary>
            private readonly List<ProjectDetailQuery> _queries = [];

            /// <summary>
            /// Initializes a fake project query service with a configured detail result.
            /// </summary>
            /// <param name="detailResult">The result returned when project detail is requested.</param>
            public FakeProjectQueryService(ProjectDetailResult detailResult)
            {
                // The fake only supports detail queries because Work Item 5 MCP tools do not call catalogue listing.
                _detailResult = detailResult ?? throw new ArgumentNullException(nameof(detailResult));
            }

            /// <summary>
            /// Gets the project detail queries received by the fake service.
            /// </summary>
            public IReadOnlyList<ProjectDetailQuery> Queries => _queries;

            /// <inheritdoc />
            public Task<ProjectCatalogueResult> ListProjectsAsync(ProjectCatalogueQuery query, CancellationToken cancellationToken)
            {
                // Catalogue listing is not expected in these MCP tool tests and fails loudly if the implementation changes unexpectedly.
                throw new NotSupportedException("Project catalogue listing is not used by MCP project tests.");
            }

            /// <inheritdoc />
            public Task<ProjectDetailResult> GetProjectAsync(ProjectDetailQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves whether validation and authorization allowed the query dependency to run.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _queries.Add(query);
                return Task.FromResult(_detailResult);
            }
        }

        /// <summary>
        /// Provides a controllable graph traversal query service for MCP dependency tests.
        /// </summary>
        private sealed class FakeGraphTraversalQueryService : IGraphTraversalQueryService
        {
            /// <summary>
            /// Stores the traversal result returned by fake traversal queries.
            /// </summary>
            private readonly GraphTraversalResult _traversalResult;

            /// <summary>
            /// Stores traversal queries received by the fake service for assertions.
            /// </summary>
            private readonly List<GraphTraversalQuery> _queries = [];

            /// <summary>
            /// Initializes a fake traversal query service with a configured traversal result.
            /// </summary>
            /// <param name="traversalResult">The result returned when traversal is requested.</param>
            public FakeGraphTraversalQueryService(GraphTraversalResult traversalResult)
            {
                // The fake supports dependency/dependent traversal; dependency-path methods are outside Work Item 5.
                _traversalResult = traversalResult ?? throw new ArgumentNullException(nameof(traversalResult));
            }

            /// <summary>
            /// Gets the traversal queries received by the fake service.
            /// </summary>
            public IReadOnlyList<GraphTraversalQuery> Queries => _queries;

            /// <inheritdoc />
            public Task<GraphTraversalResult> TraverseAsync(GraphTraversalQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves MCP traversal mapped direction, depth, filters, and limits correctly.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _queries.Add(query);
                GraphTraversalResponseDto? response = _traversalResult.Response;
                if (response is not null)
                {
                    GraphTraversalResponseDto adjustedResponse = response with
                    {
                        Direction = query.Direction ?? response.Direction,
                        Depth = query.Depth,
                        Mode = query.Mode
                    };
                    return Task.FromResult(new GraphTraversalResult(adjustedResponse, _traversalResult.Context!));
                }

                return Task.FromResult(_traversalResult);
            }

            /// <inheritdoc />
            public Task<DependencyPathResult> GetDependencyPathAsync(DependencyPathQuery query, CancellationToken cancellationToken)
            {
                // Dependency path queries belong to a later work item and are not expected here.
                throw new NotSupportedException("Dependency path queries are not used by MCP dependency traversal tests.");
            }
        }
    }
}
