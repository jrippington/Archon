using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Search;
using Archon.ServiceDefaults;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpPrompts;
using ArchonMcp.McpResources;
using ArchonMcp.McpRuntime;
using ArchonMcp.McpSearch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 operational readiness, cancellation propagation, and host-level integration across representative MCP surfaces.
    /// </summary>
    public sealed class ArchonMcpOperationalIntegrationValidationTests
    {
        /// <summary>
        /// Confirms readiness succeeds for the completed catalog and fails closed when a required prompt registration is missing.
        /// </summary>
        /// <returns>A task that completes after both readiness states have been checked.</returns>
        [Fact]
        public async Task ReadinessReflectsCompleteAndIncompleteMandatoryRegistration()
        {
            // The default host has the full Work Item 1-11 catalog, so readiness should report success.
            await using WebApplication readyApp = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await readyApp.StartAsync();
            using HttpClient readyClient = readyApp.GetTestClient();
            IArchonMcpRegistrationCatalog readyCatalog = readyApp.Services.GetRequiredService<IArchonMcpRegistrationCatalog>();

            HttpResponseMessage readyHealth = await readyClient.GetAsync(ServiceDefaultEndpointNames.Health);

            Assert.True(readyCatalog.Validate().IsReady);
            Assert.Equal(HttpStatusCode.OK, readyHealth.StatusCode);

            // Adding a required capability name that is not registered simulates incomplete registration after the catalog is introduced.
            await using WebApplication incompleteApp = Program.BuildApplication(
                ["Archon:Mcp:RegistrationCatalog:MandatoryCapabilityNames:999=archon.missing_required_prompt"],
                builder => builder.WebHost.UseTestServer());
            await incompleteApp.StartAsync();
            using HttpClient incompleteClient = incompleteApp.GetTestClient();
            IArchonMcpRegistrationCatalog incompleteCatalog = incompleteApp.Services.GetRequiredService<IArchonMcpRegistrationCatalog>();

            HttpResponseMessage incompleteHealth = await incompleteClient.GetAsync(ServiceDefaultEndpointNames.Health);
            ArchonMcpCatalogValidationResult incompleteValidation = incompleteCatalog.Validate();

            // The readiness endpoint and catalog validation should agree that missing mandatory registration keeps the host unready.
            Assert.False(incompleteValidation.IsReady);
            Assert.Contains("archon.missing_required_prompt", incompleteValidation.MissingRequiredCapabilityNames);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, incompleteHealth.StatusCode);
        }

        /// <summary>
        /// Confirms cancellation reaches query-backed MCP handlers rather than being swallowed or converted into a success envelope.
        /// </summary>
        /// <returns>A task that completes after cancellation propagation is verified.</returns>
        [Fact]
        public async Task SearchCancellationPropagatesThroughQueryAbstraction()
        {
            // The fake search service records the token it receives and throws if cancellation has already been requested.
            CancellationObservingSearchQueryService searchService = new();
            using WebApplication app = BuildSearchApp(searchService);
            IArchonMcpSearchTool searchTool = app.Services.GetRequiredService<IArchonMcpSearchTool>();
            using CancellationTokenSource cancellationSource = new();
            searchService.CancelWhenInvoked = cancellationSource;

            Task<object> operation = searchTool.SearchAsync(CreateSearchRequest(), cancellationSource.Token);

            // OperationCanceledException proves cooperative cancellation was not converted into a query-layer failure envelope.
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            Assert.True(searchService.ReceivedCanceledToken);
        }

        /// <summary>
        /// Confirms representative host-level tool, resource, and prompt calls return bounded MCP envelopes or structured errors.
        /// </summary>
        /// <returns>A task that completes after the in-memory host calls have been validated.</returns>
        [Fact]
        public async Task RepresentativeHostLevelToolResourceAndPromptCallsUseCommonContracts()
        {
            // The host-level calls exercise actual endpoint mapping while replacing only the search query seam needed for a success case.
            CapturingSearchQueryService searchService = new();
            await using WebApplication app = Program.BuildApplication(
                [
                    "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                    "Archon:Mcp:Security:TestCallerId=integration-user",
                    $"Archon:Mcp:Security:AllowedOperations:0={ArchonMcpSearchOperation.Name}",
                    $"Archon:Mcp:Security:AllowedOperations:1={ArchonMcpResourceOperations.ReadResource}",
                    $"Archon:Mcp:Security:AllowedOperations:2={ArchonMcpPromptOperations.GetPrompt}",
                    $"Archon:Mcp:Security:AllowedOperations:3={ArchonMcpPromptOperations.ListPrompts}",
                    "Archon:Mcp:Limits:MaxResultCount=1"
                ],
                builder =>
                {
                    // The in-memory server hosts the same verification endpoints used for representative MCP operation validation.
                    builder.WebHost.UseTestServer();
                    builder.Services.AddSingleton<ISearchQueryService>(searchService);
                });
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage searchResponse = await client.PostAsJsonAsync("/mcp/tools/archon.search", CreateSearchRequest());
            HttpResponseMessage resourceResponse = await client.GetAsync("/mcp/resources?uri=archon%3A%2F%2Fnot-supported%2Fcurrent");
            HttpResponseMessage promptResponse = await client.GetAsync("/mcp/prompts/impact-analysis");

            string searchJson = await searchResponse.Content.ReadAsStringAsync();
            string resourceJson = await resourceResponse.Content.ReadAsStringAsync();
            string promptJson = await promptResponse.Content.ReadAsStringAsync();

            // The successful tool response demonstrates common envelope fields and stable-key evidence without persistence details.
            Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
            AssertJsonProperty(searchJson, "operation", ArchonMcpSearchOperation.Name);
            Assert.Contains("summary", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("confidence", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("evidence", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unknowns", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("warnings", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("suggestedFollowUps", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("evidence://orders/handler", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Neo4j", searchJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, searchService.InvocationCount);

            // The unsupported resource still uses a structured MCP error instead of raw exceptions or route internals.
            Assert.Equal(HttpStatusCode.InternalServerError, resourceResponse.StatusCode);
            Assert.Contains("unsupported_operation", resourceJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.", resourceJson, StringComparison.OrdinalIgnoreCase);

            // Prompt retrieval is host-level, read-only, and returns the registered template envelope.
            Assert.Equal(HttpStatusCode.OK, promptResponse.StatusCode);
            AssertJsonProperty(promptJson, "operation", ArchonMcpPromptOperations.GetPrompt);
            Assert.Contains("impact-analysis", promptJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prompt-injection", promptJson, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds an MCP host with a cancellation-observing search query service and authorized search operation.
        /// </summary>
        /// <param name="searchService">The fake query service that records cancellation behavior.</param>
        /// <returns>A configured MCP host application for cancellation validation.</returns>
        private static WebApplication BuildSearchApp(CancellationObservingSearchQueryService searchService)
        {
            // Production composition is reused while the search query seam is replaced by a cancellation-focused test double.
            return Program.BuildApplication(
                [
                    "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                    "Archon:Mcp:Security:TestCallerId=developer-1",
                    $"Archon:Mcp:Security:AllowedOperations:0={ArchonMcpSearchOperation.Name}"
                ],
                builder => builder.Services.AddSingleton<ISearchQueryService>(searchService));
        }

        /// <summary>
        /// Creates a valid search request shared by cancellation and host integration tests.
        /// </summary>
        /// <returns>A valid MCP search request.</returns>
        private static ArchonMcpSearchRequest CreateSearchRequest()
        {
            // Stable scope fields ensure tests exercise handler execution rather than validation failure.
            return new ArchonMcpSearchRequest(
                "orders",
                "latest",
                null,
                "repository://archon-test",
                "solution://archon-test/main",
                null,
                1);
        }

        /// <summary>
        /// Asserts a top-level JSON string property has the expected value.
        /// </summary>
        /// <param name="json">The JSON payload to inspect.</param>
        /// <param name="propertyName">The top-level property name to read.</param>
        /// <param name="expectedValue">The expected string property value.</param>
        private static void AssertJsonProperty(string json, string propertyName, string expectedValue)
        {
            // System.Text.Json keeps these assertions independent of response contract concrete CLR generic types.
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.True(root.TryGetProperty(propertyName, out JsonElement property));
            Assert.Equal(expectedValue, property.GetString());
        }

        /// <summary>
        /// Records whether a canceled token reaches the query-layer search abstraction.
        /// </summary>
        private sealed class CancellationObservingSearchQueryService : ISearchQueryService
        {
            /// <summary>
            /// Gets or sets the cancellation source that the fake cancels when query execution starts.
            /// </summary>
            public CancellationTokenSource? CancelWhenInvoked { get; set; }

            /// <summary>
            /// Gets a value indicating whether the received token was already canceled.
            /// </summary>
            public bool ReceivedCanceledToken { get; private set; }

            /// <inheritdoc />
            public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
            {
                // The handler should pass the caller token to this seam and allow ThrowIfCancellationRequested to escape.
                ArgumentNullException.ThrowIfNull(query);
                CancelWhenInvoked?.Cancel();
                ReceivedCanceledToken = cancellationToken.IsCancellationRequested;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(CreateSearchResult([]));
            }
        }

        /// <summary>
        /// Provides deterministic successful search output for host-level integration validation.
        /// </summary>
        private sealed class CapturingSearchQueryService : ISearchQueryService
        {
            /// <summary>
            /// Gets the number of times search was invoked.
            /// </summary>
            public int InvocationCount { get; private set; }

            /// <inheritdoc />
            public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
            {
                // The integration test expects exactly one successful query-backed tool invocation.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                InvocationCount++;
                SearchResultItemDto item = new(
                    SearchResultKinds.Symbol,
                    "symbol://orders/handler",
                    "OrdersHandler",
                    "Handles order messages for the test fixture.",
                    "snapshot://archon-test/current",
                    0.9m,
                    ["evidence://orders/handler"],
                    ["symbol://orders/handler"],
                    HasUnknownData: false,
                    UnknownReason: null,
                    [new SearchFollowUpAffordanceDto("Describe symbol", "/query/symbol", new Dictionary<string, string> { ["stableKey"] = "symbol://orders/handler" })]);
                return Task.FromResult(CreateSearchResult([item]));
            }
        }

        /// <summary>
        /// Creates a query-layer search result with deterministic scope, snapshot, warnings, unknowns, and items.
        /// </summary>
        /// <param name="items">The result items to expose through the fake query page.</param>
        /// <returns>A successful search result.</returns>
        private static SearchResult CreateSearchResult(IReadOnlyList<SearchResultItemDto> items)
        {
            // The context includes warning and unknown collections so common envelope integration assertions can observe those sections.
            ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
            ProjectSnapshotMetadataDto snapshot = new("snapshot://archon-test/current", "latest", true, "fingerprint", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-01T00:01:00Z"), "Completed");
            SearchQueryContext context = new(
                scope,
                snapshot,
                [new SearchWarningDto("integration.warning", "Integration warning used to verify warning envelope shape.")],
                [new SearchUnknownDto("integrationUnknown", "Integration unknown used to verify unknown envelope shape.")]);
            PagedQueryResult<SearchResultItemDto> page = new(items, items.Count, skip: 0, take: Math.Max(1, items.Count));
            return new SearchResult(page, context);
        }
    }
}
