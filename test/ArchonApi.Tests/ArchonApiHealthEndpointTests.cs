using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
        /// Confirms feature endpoints still outside WP004 are not accidentally exposed by the API host.
        /// </summary>
        /// <returns>A task that completes after representative excluded endpoint paths have been checked.</returns>
        [Fact]
        public async Task FeatureEndpointsOutsideWp004AreNotMapped()
        {
            // WP004 adds extraction routes, but unrelated query, management, documentation, and UI routes remain absent.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // Representative query, management, Swagger, and human UI paths should remain absent until their own work packages.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/query")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/management")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        }
    }
}
