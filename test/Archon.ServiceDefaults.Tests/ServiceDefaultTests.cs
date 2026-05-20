using Archon.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Archon.ServiceDefaults.Tests
{
    /// <summary>
    /// Verifies that the shared service-default extension registers runtime services and maps probe endpoints.
    /// </summary>
    public sealed class ServiceDefaultTests
    {
        /// <summary>
        /// Confirms that the service-default registration adds health checks and the default HTTP client factory.
        /// </summary>
        [Fact]
        public void AddServiceDefaultsRegistersRuntimeServices()
        {
            // The test builds a normal web-application builder because service defaults target ASP.NET Core hosts in WP001.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development"
            });

            builder.AddServiceDefaults();

            using WebApplication app = builder.Build();

            // Resolving these services proves the shared registration path configured health checks and HTTP client support.
            Assert.NotNull(app.Services.GetRequiredService<HealthCheckService>());
            Assert.NotNull(app.Services.GetRequiredService<IHttpClientFactory>());
        }

        /// <summary>
        /// Confirms that default endpoints expose successful readiness and liveness probe responses through an in-memory server.
        /// </summary>
        /// <returns>A task that completes after both probe responses have been validated.</returns>
        [Fact]
        public async Task MapDefaultEndpointsReturnsSuccessfulProbeResponses()
        {
            // TestServer validates endpoint mapping without starting the Aspire AppHost or binding a real network port.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development"
            });
            builder.WebHost.UseTestServer();
            builder.AddServiceDefaults();

            await using WebApplication app = builder.Build();
            app.MapDefaultEndpoints();
            await app.StartAsync();

            using HttpClient client = app.GetTestClient();

            // Both probes should return successful responses because Work Item 2 registers only the healthy self check.
            HttpResponseMessage healthResponse = await client.GetAsync(ServiceDefaultEndpointNames.Health);
            HttpResponseMessage aliveResponse = await client.GetAsync(ServiceDefaultEndpointNames.Alive);

            Assert.True(healthResponse.IsSuccessStatusCode);
            Assert.True(aliveResponse.IsSuccessStatusCode);
        }
    }
}
