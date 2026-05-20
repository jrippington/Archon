using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies the WP001 MCP host exposes only health and readiness probe behavior.
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

            // The readiness and liveness endpoints are the complete MCP host surface for Work Item 2.
            HttpResponseMessage healthResponse = await client.GetAsync(ServiceDefaultEndpointNames.Health);
            HttpResponseMessage aliveResponse = await client.GetAsync(ServiceDefaultEndpointNames.Alive);

            Assert.True(healthResponse.IsSuccessStatusCode);
            Assert.True(aliveResponse.IsSuccessStatusCode);
        }

        /// <summary>
        /// Confirms MCP capabilities excluded from WP001 are not accidentally exposed by the MCP host.
        /// </summary>
        /// <returns>A task that completes after representative excluded endpoint paths have been checked.</returns>
        [Fact]
        public async Task McpCapabilityEndpointsAreNotMappedInWp001()
        {
            // The MCP host must remain a probe-only shell until later work packages add tools, resources, and prompts.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // Representative MCP capability paths should remain absent in Work Item 2.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/mcp/tools")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/mcp/resources")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/mcp/prompts")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/architecture")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        }
    }
}
