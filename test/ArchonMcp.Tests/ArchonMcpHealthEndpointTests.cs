using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the WP015 baseline MCP host exposes readiness and liveness probe behavior without mapping unsafe MCP endpoints.
    /// </summary>
    public sealed class ArchonMcpHealthEndpointTests
    {
        /// <summary>
        /// Confirms the MCP host readiness and liveness endpoints return successful responses through an in-memory host.
        /// </summary>
        /// <returns>A task that completes after both probe responses have been validated.</returns>
        [Fact]
        public async Task ProbeEndpointsReturnSuccessfulResponses()
        {
            // BuildApplication provides a testable seam so the host can be validated without launching the Aspire AppHost.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // The readiness and liveness endpoints are the externally mapped HTTP surface for Work Item 1.
            HttpResponseMessage healthResponse = await client.GetAsync(ServiceDefaultEndpointNames.Health);
            HttpResponseMessage aliveResponse = await client.GetAsync(ServiceDefaultEndpointNames.Alive);

            Assert.True(healthResponse.IsSuccessStatusCode);
            Assert.True(aliveResponse.IsSuccessStatusCode);
        }

        /// <summary>
        /// Confirms unsupported MCP and general-purpose capability paths are not accidentally exposed by the MCP host.
        /// </summary>
        /// <returns>A task that completes after representative excluded endpoint paths have been checked.</returns>
        [Fact]
        public async Task UnsupportedMcpCapabilityEndpointsAreNotMapped()
        {
            // The MCP host now maps narrow verification paths for implemented slices, including prompt listing and retrieval.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // Representative unsupported MCP capability paths should remain absent while resources require a validated URI.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/mcp/tools")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/mcp/resources")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/mcp/prompts")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/mcp/prompts/not-a-prompt")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/architecture")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        }
    }
}
