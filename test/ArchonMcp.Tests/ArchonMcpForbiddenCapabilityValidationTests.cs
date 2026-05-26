using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Search;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpRuntime;
using ArchonMcp.McpSearch;
using ArchonMcp.McpSecurity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the completed WP015 MCP surface does not register, expose, or execute forbidden mutation and arbitrary-execution capabilities.
    /// </summary>
    public sealed class ArchonMcpForbiddenCapabilityValidationTests
    {
        /// <summary>
        /// Confirms the composed MCP catalog contains only read-only capability registrations and no forbidden operation names.
        /// </summary>
        [Fact]
        public void CompleteCatalogContainsOnlyReadOnlyNonForbiddenCapabilities()
        {
            // The completed WP015 catalog is the authoritative capability allow-list that readiness and clients rely on.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());
            IArchonMcpRegistrationCatalog catalog = app.Services.GetRequiredService<IArchonMcpRegistrationCatalog>();

            IReadOnlyList<ArchonMcpCapabilityRegistration> registrations = catalog.GetRegistrations();
            ArchonMcpCatalogValidationResult validation = catalog.Validate();

            // A ready catalog with only read-only entries proves no registered tool, resource, or prompt advertises mutation behavior.
            Assert.True(validation.IsReady);
            Assert.Empty(validation.ForbiddenCapabilityNames);
            Assert.All(registrations, registration => Assert.True(registration.ReadOnly));
            Assert.DoesNotContain(registrations, registration => ContainsForbiddenFragment(registration.Name));
            Assert.Contains(registrations, registration => registration.Kind == ArchonMcpCapabilityKind.Tool);
            Assert.Contains(registrations, registration => registration.Kind == ArchonMcpCapabilityKind.Resource);
            Assert.Contains(registrations, registration => registration.Kind == ArchonMcpCapabilityKind.Prompt);
        }

        /// <summary>
        /// Confirms representative forbidden capability names and non-read-only registrations fail catalog validation.
        /// </summary>
        /// <param name="capabilityName">The unsafe capability name to verify.</param>
        [Theory]
        [InlineData("archon.execute_shell")]
        [InlineData("archon.run_sql")]
        [InlineData("archon.query_cypher")]
        [InlineData("archon.graph_query")]
        [InlineData("archon.filesystem_write")]
        [InlineData("archon.source_code_mutation")]
        [InlineData("archon.database_update")]
        [InlineData("archon.rule_delete")]
        [InlineData("archon.finding_mutate")]
        [InlineData("archon.snapshot_delete")]
        public void CatalogValidationRejectsForbiddenOperationNames(string capabilityName)
        {
            // Each name represents a class of forbidden capability from the WP015 security acceptance criteria.
            ArchonMcpCapabilityRegistration unsafeRegistration = new(
                capabilityName,
                ArchonMcpCapabilityKind.Tool,
                Required: false,
                ReadOnly: true,
                "Unsafe test-only registration that must keep the catalog unready.");
            ArchonMcpRegistrationCatalog catalog = new(
                [ArchonMcpBaselineCapabilities.Health, unsafeRegistration],
                Options.Create(new ArchonMcpRegistrationCatalogOptions
                {
                    MandatoryCapabilityNames = [ArchonMcpBaselineCapabilities.Health.Name]
                }));

            ArchonMcpCatalogValidationResult validation = catalog.Validate();

            // Readiness must fail closed and report the exact unsafe capability name without executing any operation.
            Assert.False(validation.IsReady);
            Assert.Empty(validation.MissingRequiredCapabilityNames);
            Assert.Contains(capabilityName, validation.ForbiddenCapabilityNames);
        }

        /// <summary>
        /// Confirms a non-read-only registration fails validation even when its name does not contain a forbidden word.
        /// </summary>
        [Fact]
        public void CatalogValidationRejectsNonReadOnlyRegistration()
        {
            // A benign-looking name cannot bypass the read-only contract if the registration itself is marked mutating.
            ArchonMcpCapabilityRegistration mutatingRegistration = new(
                "archon.review_project",
                ArchonMcpCapabilityKind.Tool,
                Required: false,
                ReadOnly: false,
                "Unsafe test-only registration that is not read-only.");
            ArchonMcpRegistrationCatalog catalog = new(
                [ArchonMcpBaselineCapabilities.Health, mutatingRegistration],
                Options.Create(new ArchonMcpRegistrationCatalogOptions
                {
                    MandatoryCapabilityNames = [ArchonMcpBaselineCapabilities.Health.Name]
                }));

            ArchonMcpCatalogValidationResult validation = catalog.Validate();

            // Non-read-only capabilities are forbidden because the MCP host is constrained to investigation-only behavior.
            Assert.False(validation.IsReady);
            Assert.Contains("archon.review_project", validation.ForbiddenCapabilityNames);
        }

        /// <summary>
        /// Confirms unsupported HTTP command, query, and mutation paths fail closed instead of exposing general-purpose execution surfaces.
        /// </summary>
        /// <returns>A task that completes after representative forbidden paths are verified.</returns>
        [Fact]
        public async Task UnsupportedCommandQueryAndMutationHttpRequestsFailClosed()
        {
            // The host maps only narrow verification endpoints; unsupported execution-style paths should remain absent.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage shellResponse = await client.PostAsync("/mcp/tools/archon.execute_shell", JsonContent.Create(new { command = "whoami" }));
            HttpResponseMessage sqlResponse = await client.PostAsync("/mcp/tools/archon.run_sql", JsonContent.Create(new { query = "select * from secrets" }));
            HttpResponseMessage cypherResponse = await client.PostAsync("/mcp/tools/archon.query_cypher", JsonContent.Create(new { query = "match (n) return n" }));
            HttpResponseMessage fileMutationResponse = await client.PostAsync("/mcp/tools/archon.write_file", JsonContent.Create(new { path = "src/Program.cs" }));
            HttpResponseMessage ruleMutationResponse = await client.PostAsync("/mcp/tools/archon.rule_delete", JsonContent.Create(new { ruleCode = "ARCH001" }));
            HttpResponseMessage findingMutationResponse = await client.PostAsync("/mcp/tools/archon.finding_suppress", JsonContent.Create(new { finding = "finding://one" }));
            HttpResponseMessage snapshotMutationResponse = await client.PostAsync("/mcp/tools/archon.snapshot_delete", JsonContent.Create(new { snapshot = "snapshot://one" }));

            // NotFound proves the routes are absent; they are not mapped to hidden shell, SQL, graph, file, rule, finding, or snapshot behavior.
            Assert.Equal(HttpStatusCode.NotFound, shellResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, sqlResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, cypherResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, fileMutationResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, ruleMutationResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, findingMutationResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, snapshotMutationResponse.StatusCode);
        }

        /// <summary>
        /// Confirms unauthorized and disabled operations fail before query dependencies are invoked.
        /// </summary>
        /// <returns>A task that completes after both fail-closed security paths are verified.</returns>
        [Fact]
        public async Task UnauthorizedAndDisabledToolRequestsDoNotInvokeQueryDependencies()
        {
            // The same search handler represents a real query-backed MCP tool and exposes captured query calls for assertions.
            CapturingSearchQueryService unauthorizedSearch = new();
            using WebApplication unauthorizedApp = BuildSearchApp(unauthorizedSearch, callerId: string.Empty, allowedOperations: [ArchonMcpSearchOperation.Name]);
            IArchonMcpSearchTool unauthorizedTool = unauthorizedApp.Services.GetRequiredService<IArchonMcpSearchTool>();

            object unauthorizedPayload = await unauthorizedTool.SearchAsync(CreateSearchRequest(), CancellationToken.None);

            // Missing authentication must stop before validation reaches the query abstraction.
            ArchonMcpErrorResponse unauthorizedError = Assert.IsType<ArchonMcpErrorResponse>(unauthorizedPayload);
            Assert.Equal(ArchonMcpErrorCategory.Unauthorized, unauthorizedError.Error.Category);
            Assert.Equal(0, unauthorizedSearch.InvocationCount);

            CapturingSearchQueryService disabledSearch = new();
            using WebApplication disabledApp = BuildSearchApp(disabledSearch, callerId: "developer-1", allowedOperations: [ArchonMcpBaselineCapabilities.Health.Name]);
            IArchonMcpSearchTool disabledTool = disabledApp.Services.GetRequiredService<IArchonMcpSearchTool>();

            object disabledPayload = await disabledTool.SearchAsync(CreateSearchRequest(), CancellationToken.None);

            // Disabled operation allow-listing must stop before the query abstraction even for an authenticated caller.
            ArchonMcpErrorResponse disabledError = Assert.IsType<ArchonMcpErrorResponse>(disabledPayload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, disabledError.Error.Category);
            Assert.Equal(0, disabledSearch.InvocationCount);
        }

        /// <summary>
        /// Builds an MCP host with a capturing search service and configurable security settings.
        /// </summary>
        /// <param name="searchService">The query-layer test double that records attempted search execution.</param>
        /// <param name="callerId">The caller identifier supplied by the configuration-backed caller provider.</param>
        /// <param name="allowedOperations">The operation allow-list used by the configuration-backed authorizer.</param>
        /// <returns>A configured MCP host application for security validation tests.</returns>
        private static WebApplication BuildSearchApp(CapturingSearchQueryService searchService, string callerId, string[] allowedOperations)
        {
            // Production composition is reused so the test proves authorization behavior at the same seam as real handlers.
            List<string> args =
            [
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                $"Archon:Mcp:Security:TestCallerId={callerId}"
            ];
            for (int index = 0; index < allowedOperations.Length; index++)
            {
                args.Add($"Archon:Mcp:Security:AllowedOperations:{index}={allowedOperations[index]}");
            }

            return Program.BuildApplication(args.ToArray(), builder => builder.Services.AddSingleton<ISearchQueryService>(searchService));
        }

        /// <summary>
        /// Creates a valid search request so security checks are the only reason a query-backed tool cannot execute.
        /// </summary>
        /// <returns>A valid MCP search request.</returns>
        private static ArchonMcpSearchRequest CreateSearchRequest()
        {
            // Stable repository and solution scopes keep validation from masking authorization-first behavior.
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
        /// Determines whether a capability name contains a forbidden operation fragment documented by WP015.
        /// </summary>
        /// <param name="name">The registered capability name to inspect.</param>
        /// <returns><see langword="true" /> when the name implies a forbidden capability; otherwise, <see langword="false" />.</returns>
        private static bool ContainsForbiddenFragment(string name)
        {
            // This test-side vocabulary mirrors the Work Item 12 acceptance criteria rather than production implementation details.
            string[] forbiddenFragments =
            [
                "shell",
                "sql",
                "cypher",
                "graph_query",
                "filesystem",
                "file_system",
                "source_code_mutation",
                "database_mutation",
                "rule_mutation",
                "finding_mutation",
                "snapshot_mutation",
                "mutate",
                "write",
                "delete",
                "update",
                "execute",
                "exec",
                "command",
                "code_modification",
                "code-edit",
                "code_edit"
            ];

            return forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Records attempted query-layer search execution while returning an empty successful page if invoked.
        /// </summary>
        private sealed class CapturingSearchQueryService : ISearchQueryService
        {
            /// <summary>
            /// Gets the number of times the fake query service was invoked.
            /// </summary>
            public int InvocationCount { get; private set; }

            /// <inheritdoc />
            public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
            {
                // Invocation should remain zero for unauthorized or disabled operation tests; if called, return a safe empty page.
                ArgumentNullException.ThrowIfNull(query);
                cancellationToken.ThrowIfCancellationRequested();
                InvocationCount++;
                ProjectScopeDto scope = new("repository://archon-test", "Archon Test", "solution://archon-test/main", "Archon Test Solution");
                ProjectSnapshotMetadataDto snapshot = new("snapshot://archon-test/current", "latest", true, "fingerprint", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-01T00:01:00Z"), "Completed");
                SearchQueryContext context = new(scope, snapshot, [], [new SearchUnknownDto("searchResults", "No records were returned by the capturing fake.")]);
                PagedQueryResult<SearchResultItemDto> page = new([], 0, skip: 0, take: 1);
                return Task.FromResult(new SearchResult(page, context));
            }
        }
    }
}
