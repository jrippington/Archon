using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using Xunit;

namespace ArchonApi.Tests
{
    /// <summary>
    /// Verifies the WP001 API host exposes only health and readiness probe behavior.
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

            // The readiness and liveness endpoints are the complete API surface for Work Item 2.
            HttpResponseMessage healthResponse = await client.GetAsync(ServiceDefaultEndpointNames.Health);
            HttpResponseMessage aliveResponse = await client.GetAsync(ServiceDefaultEndpointNames.Alive);

            Assert.True(healthResponse.IsSuccessStatusCode);
            Assert.True(aliveResponse.IsSuccessStatusCode);
        }

        /// <summary>
        /// Confirms feature endpoints excluded from WP001 are not accidentally exposed by the API host.
        /// </summary>
        /// <returns>A task that completes after representative excluded endpoint paths have been checked.</returns>
        [Fact]
        public async Task FeatureEndpointsAreNotMappedInWp001()
        {
            // The API host must remain a health-only shell until later work packages add feature modules.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // Representative extraction, query, management, Swagger, Scalar, and UI paths should remain absent in Work Item 2.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/extractions")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/query")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/management")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        }
    }
}
