using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Net;
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

            // Representative query, management, Swagger, Scalar, and UI paths should remain absent until their own work packages.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/query")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/management")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        }
    }
}
