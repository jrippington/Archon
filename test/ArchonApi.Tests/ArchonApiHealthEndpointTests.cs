using Archon.Application.Graph.Persistence;
using Archon.Application.Extraction.Runs;
using Archon.Infrastructure.Neo4j.Configuration;
using Archon.Infrastructure.Neo4j.Persistence;
using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Text.Json;
using Xunit;

namespace ArchonApi.Tests
{
    /// <summary>
    /// Verifies the API host exposes operational probes while composing the currently implemented feature modules.
    /// </summary>
    public sealed class ArchonApiHealthEndpointTests
    {
        /// <summary>
        /// Confirms the API host readiness and liveness endpoints return successful responses through an in-memory host.
        /// </summary>
        /// <returns>A task that completes after both probe responses have been validated.</returns>
        [Fact]
        public async Task ProbeEndpointsReturnSuccessfulResponses()
        {
            // BuildApplication provides a testable seam so the host can be validated without launching the Aspire AppHost.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // The readiness and liveness endpoints remain available as feature endpoints are added in later work packages.
            HttpResponseMessage healthResponse = await client.GetAsync(ServiceDefaultEndpointNames.Health);
            HttpResponseMessage aliveResponse = await client.GetAsync(ServiceDefaultEndpointNames.Alive);

            Assert.True(healthResponse.IsSuccessStatusCode);
            Assert.True(aliveResponse.IsSuccessStatusCode);
        }

        /// <summary>
        /// Confirms the development host exposes browsable Scalar and machine-readable OpenAPI documentation for implemented APIs.
        /// </summary>
        /// <returns>A task that completes after both documentation endpoint responses have been validated.</returns>
        [Fact]
        public async Task DocumentationEndpointsReturnSuccessfulResponsesInDevelopment()
        {
            // Scalar is a development-time browser for the OpenAPI document that describes the currently mapped API modules.
            await using WebApplication app = Program.BuildApplication(["--environment", "Development"], builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            HttpResponseMessage openApiResponse = await client.GetAsync("/openapi/v1.json");
            HttpResponseMessage scalarResponse = await client.GetAsync("/scalar/v1");
            string openApiJson = await openApiResponse.Content.ReadAsStringAsync();

            Assert.True(openApiResponse.IsSuccessStatusCode);
            Assert.True(scalarResponse.IsSuccessStatusCode);

            using JsonDocument document = JsonDocument.Parse(openApiJson);
            JsonElement paths = document.RootElement.GetProperty("paths");
            Assert.True(paths.TryGetProperty("/extractions", out JsonElement extractionsPath));
            Assert.Equal("List recent extraction runs", extractionsPath.GetProperty("get").GetProperty("summary").GetString());
            Assert.Equal("Start an architecture extraction run", extractionsPath.GetProperty("post").GetProperty("summary").GetString());
            Assert.True(paths.TryGetProperty("/extractions/{runId}", out JsonElement statusPath));
            Assert.Equal("Get extraction run status", statusPath.GetProperty("get").GetProperty("summary").GetString());
        }

        /// <summary>
        /// Confirms the WP014 query and management endpoints are present in the development OpenAPI document with discoverable metadata.
        /// </summary>
        /// <returns>A task that completes after representative API documentation operations have been validated.</returns>
        [Fact]
        public async Task DocumentationEndpoint_WhenDevelopmentHostStarts_ShouldDescribeWp014QueryAndManagementSurface()
        {
            // The development OpenAPI document is the machine-readable input behind Scalar, so it must describe representative query and management routes.
            await using WebApplication app = Program.BuildApplication(["--environment", "Development"], builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            using JsonDocument document = await GetOpenApiDocumentAsync(client);
            JsonElement paths = document.RootElement.GetProperty("paths");

            // Representative routes cover non-paged envelopes, paged envelopes, traversal, search, management commands, and operations probes.
            AssertOperationHasMetadata(paths, "/dashboard-summary", "get", "Get dashboard summary data", "Dashboard");
            AssertOperationHasMetadata(paths, "/projects", "get", "List projects in a selected architecture snapshot", "Projects");
            AssertOperationHasMetadata(paths, "/graph-neighbourhood", "get", "Get a bounded graph neighbourhood around one node", "GraphTraversal");
            AssertOperationHasMetadata(paths, "/search", "get", "Search supported architecture records in a selected snapshot", "Search");
            AssertOperationHasMetadata(paths, "/management/repositories", "post", "Register repository metadata", "Management");
            AssertOperationHasMetadata(paths, "/management/retention", "post", "Validate or apply snapshot retention", "Management");
            AssertOperationHasMetadata(paths, "/ready", "get", "Get management module readiness", "Operations");
        }

        /// <summary>
        /// Confirms representative WP014 documented operations expose shared success and safe error response metadata.
        /// </summary>
        /// <returns>A task that completes after representative OpenAPI response metadata has been validated.</returns>
        [Fact]
        public async Task DocumentationEndpoint_WhenDevelopmentHostStarts_ShouldDescribeSharedResponseAndErrorContracts()
        {
            // Contract consistency tests protect automation clients by proving common route families advertise success, validation, and safe error shapes.
            await using WebApplication app = Program.BuildApplication(["--environment", "Development"], builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            using JsonDocument document = await GetOpenApiDocumentAsync(client);
            JsonElement paths = document.RootElement.GetProperty("paths");

            // Non-paged query endpoints should describe the common envelope plus validation and safe server-error responses.
            JsonElement dashboardResponses = GetOperation(paths, "/dashboard-summary", "get").GetProperty("responses");
            AssertResponseContentReferencesSchema(dashboardResponses, "200", "QueryApiResponseOfDashboardSummaryDto");
            Assert.True(dashboardResponses.TryGetProperty("400", out _));
            AssertResponseContentReferencesSchema(dashboardResponses, "500", "QueryErrorResponse");

            // Paged query endpoints should describe the paged envelope plus validation and safe server-error responses.
            JsonElement projectsResponses = GetOperation(paths, "/projects", "get").GetProperty("responses");
            AssertResponseContentReferencesSchema(projectsResponses, "200", "QueryPagedApiResponseOfProjectCatalogueItemDto");
            Assert.True(projectsResponses.TryGetProperty("400", out _));
            AssertResponseContentReferencesSchema(projectsResponses, "500", "QueryErrorResponse");

            // Management command endpoints should document success, validation failure, and server failure boundaries.
            JsonElement repositoryResponses = GetOperation(paths, "/management/repositories", "post").GetProperty("responses");
            AssertResponseContentReferencesSchema(repositoryResponses, "200", "RepositoryRegistrationResponse");
            Assert.True(repositoryResponses.TryGetProperty("400", out _));
            Assert.True(repositoryResponses.TryGetProperty("500", out _));
        }

        /// <summary>
        /// Confirms excluded documentation and UI endpoints are not accidentally exposed by the API host.
        /// </summary>
        /// <returns>A task that completes after representative excluded endpoint paths have been checked.</returns>
        [Fact]
        public async Task SwaggerAndDiscoveryUiEndpointsAreNotMapped()
        {
            // WP014 requires Scalar for interactive documentation and still excludes Swagger UI and product Discovery UI routes.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // Representative Swagger and human UI paths should remain absent from the production-style host.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/query")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        }

        /// <summary>
        /// Confirms production-style host composition uses Neo4j persistence when Neo4j configuration is supplied.
        /// </summary>
        [Fact]
        public void BuildApplication_WhenNeo4jConfigurationExists_ShouldRegisterNeo4jSnapshotLifecycleStores()
        {
            // The API host should write extraction snapshots and run lifecycle data to Neo4j under AppHost configuration rather than silently using memory-only stores.
            using WebApplication app = Program.BuildApplication(
                [
                    $"--{Neo4jOptions.SectionName}:Uri=bolt://localhost:7687",
                    $"--{Neo4jOptions.SectionName}:Database=neo4j",
                    $"--{Neo4jOptions.SectionName}:Username=neo4j",
                    $"--{Neo4jOptions.SectionName}:Password=local-development-password",
                    $"--{Neo4jOptions.SectionName}:EncryptionMode={nameof(Neo4jEncryptionMode.Unencrypted)}"
                ],
                builder => builder.WebHost.UseTestServer());

            IArchitectureSnapshotWriter writer = app.Services.GetRequiredService<IArchitectureSnapshotWriter>();
            ISnapshotLifecycleQuery lifecycleQuery = app.Services.GetRequiredService<ISnapshotLifecycleQuery>();
            ISnapshotDeletionStore deletionStore = app.Services.GetRequiredService<ISnapshotDeletionStore>();
            IExtractionRunHistory runHistory = app.Services.GetRequiredService<IExtractionRunHistory>();

            Assert.IsType<Neo4jArchitectureSnapshotWriter>(writer);
            Assert.IsType<Neo4jSnapshotLifecycleQuery>(lifecycleQuery);
            Assert.IsType<Neo4jSnapshotDeletionStore>(deletionStore);
            Assert.IsType<Neo4jExtractionRunHistory>(runHistory);
        }

        /// <summary>
        /// Reads and parses the generated OpenAPI document from a development test host.
        /// </summary>
        /// <param name="client">The in-memory HTTP client that sends requests to the test host.</param>
        /// <returns>The parsed JSON document returned by the OpenAPI endpoint.</returns>
        private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client)
        {
            // Centralizing OpenAPI reads keeps documentation tests consistent and ensures failed document generation surfaces as HTTP failures.
            HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
            response.EnsureSuccessStatusCode();
            string openApiJson = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(openApiJson);
        }

        /// <summary>
        /// Asserts that one documented operation has summary, description, and tag metadata suitable for Scalar display.
        /// </summary>
        /// <param name="paths">The OpenAPI paths object that contains all documented route operations.</param>
        /// <param name="path">The exact route template to inspect.</param>
        /// <param name="method">The lowercase HTTP method to inspect.</param>
        /// <param name="expectedSummary">The expected operation summary from route metadata.</param>
        /// <param name="expectedTag">The expected Scalar grouping tag from route metadata.</param>
        private static void AssertOperationHasMetadata(JsonElement paths, string path, string method, string expectedSummary, string expectedTag)
        {
            // Scalar renders these OpenAPI fields directly, so each representative operation must carry human-readable metadata.
            JsonElement operation = GetOperation(paths, path, method);
            Assert.Equal(expectedSummary, operation.GetProperty("summary").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
            Assert.Contains(operation.GetProperty("tags").EnumerateArray(), tag => tag.GetString() == expectedTag);
        }

        /// <summary>
        /// Gets one operation object from the generated OpenAPI paths section.
        /// </summary>
        /// <param name="paths">The OpenAPI paths object that contains route templates.</param>
        /// <param name="path">The exact route template to inspect.</param>
        /// <param name="method">The lowercase HTTP method to inspect.</param>
        /// <returns>The OpenAPI operation object for the requested route and method.</returns>
        private static JsonElement GetOperation(JsonElement paths, string path, string method)
        {
            // Explicit assertion messages make route-documentation regressions easier to diagnose than a raw KeyNotFound-style failure.
            Assert.True(paths.TryGetProperty(path, out JsonElement pathItem), $"OpenAPI path '{path}' was not documented.");
            Assert.True(pathItem.TryGetProperty(method, out JsonElement operation), $"OpenAPI method '{method}' for path '{path}' was not documented.");
            return operation;
        }

        /// <summary>
        /// Asserts that a documented response has JSON content that references the expected component schema.
        /// </summary>
        /// <param name="responses">The OpenAPI responses object for one operation.</param>
        /// <param name="statusCode">The response status code to inspect.</param>
        /// <param name="schemaNameFragment">The schema-name fragment expected in the JSON schema reference.</param>
        private static void AssertResponseContentReferencesSchema(JsonElement responses, string statusCode, string schemaNameFragment)
        {
            // The schema reference is the stable OpenAPI bridge from route metadata to the DTO contract displayed in Scalar.
            Assert.True(responses.TryGetProperty(statusCode, out JsonElement response), $"Response '{statusCode}' was not documented.");
            JsonElement content = response.GetProperty("content").GetProperty("application/json");
            string? schemaReference = content.GetProperty("schema").GetProperty("$ref").GetString();
            Assert.Contains(schemaNameFragment, schemaReference, StringComparison.Ordinal);
        }
    }
}
