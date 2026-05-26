using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Search;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSearch;
using ArchonMcp.McpSecurity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the WP015 <c>archon.search</c> MCP tool contract, security flow, query mapping, and envelope behavior.
    /// </summary>
    public sealed class ArchonMcpSearchTests
    {
        /// <summary>
        /// Confirms a successful search returns deterministic grouped facts, evidence references, snapshot identity, and follow-ups.
        /// </summary>
        [Fact]
        public async Task SearchReturnsEvidenceBackedGroupedResults()
        {
            // The fake query service represents the approved application/query seam; the MCP tool must not bypass it.
            FakeSearchQueryService searchService = new(CreateSearchResult([
                CreateItem(SearchResultKinds.Symbol, "symbol://orders/handler", "OrdersHandler", "Handles order messages.", ["evidence://source/orders-handler"], confidence: 0.91m),
                CreateItem(SearchResultKinds.Project, "project://src/orders/orders.csproj", "Orders", "Owns order processing.", ["evidence://project/orders"], confidence: 0.87m)]));
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("orders"), CancellationToken.None);

            // The envelope proves the handler mapped query DTOs into stable MCP output without exposing persistence internals.
            ArchonMcpEnvelope<ArchonMcpSearchFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSearchFacts>>(payload);
            Assert.Equal(ArchonMcpSearchOperation.Name, envelope.Operation);
            Assert.Equal("snapshot://repo/main", envelope.Snapshot?.StableKey);
            Assert.Equal(2, envelope.Facts.TotalMatches);
            Assert.Equal(2, envelope.Facts.ReturnedMatches);
            Assert.True(envelope.Facts.DataAvailable);
            Assert.Collection(
                envelope.Facts.Groups,
                group => Assert.Equal(SearchResultKinds.Project, group.ResultKind),
                group => Assert.Equal(SearchResultKinds.Symbol, group.ResultKind));
            Assert.Contains(envelope.Evidence, evidence => evidence.StableKey == "evidence://source/orders-handler");
            Assert.Equal(ArchonMcpConfidenceLevel.High, envelope.Confidence.Level);
            Assert.NotEmpty(envelope.SuggestedFollowUps);
            Assert.Single(searchService.Queries);
            Assert.Equal("orders", searchService.Queries[0].SearchText);
        }

        /// <summary>
        /// Confirms no matches are returned as a successful empty search with explicit unknowns rather than an unavailable-data error.
        /// </summary>
        [Fact]
        public async Task SearchDistinguishesNoMatchesFromUnavailableData()
        {
            // A successful empty page means search data existed but no records matched the supplied text.
            FakeSearchQueryService searchService = new(CreateSearchResult([]));
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("does-not-exist"), CancellationToken.None);

            // The success envelope distinguishes known absence from missing repository or snapshot data.
            ArchonMcpEnvelope<ArchonMcpSearchFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSearchFacts>>(payload);
            Assert.Empty(envelope.Facts.Groups);
            Assert.True(envelope.Facts.DataAvailable);
            Assert.Contains("No persisted architecture records matched", envelope.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(envelope.Unknowns, unknown => unknown.Kind == "searchResults");
        }

        /// <summary>
        /// Confirms missing repository or snapshot data is returned as a dependency-unavailable MCP error.
        /// </summary>
        [Fact]
        public async Task SearchDistinguishesUnavailableDataFromNoMatches()
        {
            // Repository-not-found is the application/query signal that the requested search scope has no available data.
            FakeSearchQueryService searchService = new(new SearchResult([
                new SearchQueryValidationError(SearchQueryValidationCodes.RepositoryNotFound, "The requested repository scope was not found.")]
            ));
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("orders"), CancellationToken.None);

            // Unavailable data uses a structured error instead of an empty success envelope.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.DependencyUnavailable, error.Error.Category);
            Assert.Contains("unavailable", error.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms invalid MCP search input fails before the query-layer service is invoked.
        /// </summary>
        [Fact]
        public async Task SearchValidationFailureDoesNotInvokeQueryLayer()
        {
            // Empty search text is rejected by the MCP validator before application/query search can execute.
            FakeSearchQueryService searchService = new(CreateSearchResult([]));
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("   "), CancellationToken.None);

            // The validation error proves malformed requests stop at the MCP boundary.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Validation, error.Error.Category);
            Assert.Contains("Search text", error.Error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(searchService.Queries);
        }

        /// <summary>
        /// Confirms disabled <c>archon.search</c> requests are forbidden before validation or query execution occurs.
        /// </summary>
        [Fact]
        public async Task DisabledSearchReturnsForbiddenBeforeQueryLayerIsInvoked()
        {
            // The configured allow-list omits archon.search so the operation executor must fail closed before handler logic.
            FakeSearchQueryService searchService = new(CreateSearchResult([]));
            using WebApplication app = BuildSearchApp(searchService, allowedOperations: ["archon.health"]);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("orders"), CancellationToken.None);

            // Forbidden output and zero queries prove the existing security seam protects the search handler.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
            Assert.Empty(searchService.Queries);
        }

        /// <summary>
        /// Confirms unauthenticated search requests return unauthorized before the query layer is invoked.
        /// </summary>
        [Fact]
        public async Task MissingCallerReturnsUnauthorizedBeforeSearchQueryLayerIsInvoked()
        {
            // An empty caller identity simulates a missing authenticated MCP principal.
            FakeSearchQueryService searchService = new(CreateSearchResult([]));
            using WebApplication app = BuildSearchApp(searchService, callerId: string.Empty);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("orders"), CancellationToken.None);

            // Unauthorized output and zero queries prove authentication runs before query work.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Unauthorized, error.Error.Category);
            Assert.Empty(searchService.Queries);
        }

        /// <summary>
        /// Confirms result truncation is reported through common MCP limit metadata, warnings, and narrowing follow-ups.
        /// </summary>
        [Fact]
        public async Task SearchReportsTruncationWhenLimitOmitsResults()
        {
            // Three results with a two-item MCP limit should preserve deterministic ordering and report truncation.
            FakeSearchQueryService searchService = new(CreateSearchResult([
                CreateItem(SearchResultKinds.Project, "project://src/a/a.csproj", "A", "A project.", ["evidence://a"], confidence: 0.9m),
                CreateItem(SearchResultKinds.Project, "project://src/b/b.csproj", "B", "B project.", ["evidence://b"], confidence: 0.9m),
                CreateItem(SearchResultKinds.Symbol, "symbol://c", "C", "C symbol.", ["evidence://c"], confidence: 0.9m)]));
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("project", limit: 2), CancellationToken.None);

            // Truncation metadata warns the AI client not to treat the response as complete.
            ArchonMcpEnvelope<ArchonMcpSearchFacts> envelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpSearchFacts>>(payload);
            Assert.True(envelope.Limits.Truncated);
            Assert.Equal(3, envelope.Limits.OriginalCount);
            Assert.Equal(2, envelope.Facts.ReturnedMatches);
            Assert.Contains(envelope.Warnings, warning => warning.Code == "mcp.search.truncated");
            Assert.Contains(envelope.SuggestedFollowUps, followUp => followUp.Label.Contains("Narrow", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms unexpected query-layer exceptions are converted into safe query-layer failure errors.
        /// </summary>
        [Fact]
        public async Task SearchQueryLayerFailureReturnsSafeError()
        {
            // Throwing from the fake query service simulates an infrastructure-backed query failure without exposing internals.
            FakeSearchQueryService searchService = new(new InvalidOperationException("Sensitive stack detail should not appear."));
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool tool = app.Services.GetRequiredService<IArchonMcpSearchTool>();

            object payload = await tool.SearchAsync(CreateRequest("orders"), CancellationToken.None);

            // The MCP error category is specific while the message omits the thrown exception details.
            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.QueryLayerFailure, error.Error.Category);
            Assert.DoesNotContain("Sensitive", error.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds an MCP host application with a test search service and security configuration.
        /// </summary>
        /// <param name="searchService">The fake query-layer service to register for the test.</param>
        /// <param name="allowedOperations">The optional operation allow-list for security-path tests.</param>
        /// <param name="callerId">The test caller identifier used by the default caller-context provider.</param>
        /// <returns>A configured web application that exposes MCP search services.</returns>
        private static WebApplication BuildSearchApp(FakeSearchQueryService searchService, string[]? allowedOperations = null, string? callerId = "developer-1")
        {
            // Tests configure the same host composition as production and replace only the query-layer abstraction.
            List<string> args =
            [
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                $"Archon:Mcp:Security:TestCallerId={callerId}",
                "Archon:Mcp:Limits:MaxResultCount=2"
            ];
            string[] operations = allowedOperations ?? ["archon.health", ArchonMcpSearchOperation.Name];
            for (int index = 0; index < operations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={operations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder => builder.Services.AddSingleton<ISearchQueryService>(searchService));
        }

        /// <summary>
        /// Creates a valid MCP search request for test scenarios.
        /// </summary>
        /// <param name="searchText">The search text to include in the request.</param>
        /// <param name="limit">The optional MCP result limit.</param>
        /// <returns>A valid search request unless the supplied text is intentionally invalid.</returns>
        private static ArchonMcpSearchRequest CreateRequest(string searchText, int? limit = null)
        {
            // The stable-key scope values satisfy both MCP validation and query-layer selector requirements.
            return new ArchonMcpSearchRequest(
                searchText,
                "latest",
                null,
                "repository://archon-test",
                "solution://archon-test/main",
                null,
                limit);
        }

        /// <summary>
        /// Creates a successful query-layer search result for the supplied items.
        /// </summary>
        /// <param name="items">The ordered query-layer items to expose through the result page.</param>
        /// <returns>A successful search result with deterministic scope and snapshot context.</returns>
        private static SearchResult CreateSearchResult(IReadOnlyList<SearchResultItemDto> items)
        {
            // The result context mirrors the application/query DTO shape produced by the real search service.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://repo/main", "latest", true, "abc123", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), DateTimeOffset.Parse("2025-01-01T00:01:00Z"), "Completed");
            SearchQueryContext context = items.Count == 0
                ? new SearchQueryContext(scope, snapshot, [], [new SearchUnknownDto("searchResults", "No supported persisted record matched the supplied search text and filters.")])
                : new SearchQueryContext(scope, snapshot, [], []);
            PagedQueryResult<SearchResultItemDto> page = new(items, items.Count, skip: 0, take: Math.Max(1, items.Count));
            return new SearchResult(page, context);
        }

        /// <summary>
        /// Creates one deterministic query-layer search item for mapping tests.
        /// </summary>
        /// <param name="kind">The controlled search result kind.</param>
        /// <param name="stableKey">The stable public identity of the result.</param>
        /// <param name="displayText">The result display text.</param>
        /// <param name="summary">The safe result summary.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys supporting the result.</param>
        /// <param name="confidence">The numeric query-layer confidence value.</param>
        /// <returns>A query-layer search result item.</returns>
        private static SearchResultItemDto CreateItem(string kind, string stableKey, string displayText, string summary, IReadOnlyList<string> evidenceStableKeys, decimal confidence)
        {
            // Follow-up routes use stable keys and approved API-style paths rather than arbitrary shell or graph-query instructions.
            SearchFollowUpAffordanceDto followUp = new("Inspect matched record", "/query/search/follow-up", new Dictionary<string, string> { ["stableKey"] = stableKey });
            return new SearchResultItemDto(
                kind,
                stableKey,
                displayText,
                summary,
                "snapshot://repo/main",
                confidence,
                evidenceStableKeys,
                [stableKey],
                HasUnknownData: false,
                UnknownReason: null,
                [followUp]);
        }

        /// <summary>
        /// Provides a controllable query-layer search service for MCP search tests.
        /// </summary>
        private sealed class FakeSearchQueryService : ISearchQueryService
        {
            /// <summary>
            /// Stores the result returned by successful fake query execution.
            /// </summary>
            private readonly SearchResult? _result;

            /// <summary>
            /// Stores the exception thrown by fake query execution when failure behavior is under test.
            /// </summary>
            private readonly Exception? _exception;

            /// <summary>
            /// Initializes a fake search service that returns a configured result.
            /// </summary>
            /// <param name="result">The result returned when search is invoked.</param>
            public FakeSearchQueryService(SearchResult result)
            {
                // Result mode supports success, no-match, and query-layer validation scenarios.
                _result = result ?? throw new ArgumentNullException(nameof(result));
            }

            /// <summary>
            /// Initializes a fake search service that throws a configured exception.
            /// </summary>
            /// <param name="exception">The exception thrown when search is invoked.</param>
            public FakeSearchQueryService(Exception exception)
            {
                // Exception mode verifies safe query-layer failure mapping.
                _exception = exception ?? throw new ArgumentNullException(nameof(exception));
            }

            /// <summary>
            /// Gets the queries received by the fake service.
            /// </summary>
            public IReadOnlyList<SearchQuery> Queries => _queries;

            /// <summary>
            /// Stores the queries received by the fake service for assertion.
            /// </summary>
            private readonly List<SearchQuery> _queries = [];

            /// <inheritdoc />
            public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
            {
                // Capturing the query proves whether validation and security allowed the application/query dependency to run.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                _queries.Add(query);
                if (_exception is not null)
                {
                    throw _exception;
                }

                return Task.FromResult(_result ?? throw new InvalidOperationException("No fake search result was configured."));
            }
        }
    }
}
