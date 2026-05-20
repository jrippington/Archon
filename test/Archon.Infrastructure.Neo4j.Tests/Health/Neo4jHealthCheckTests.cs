using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Health;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Health
{
    /// <summary>
    /// Verifies Neo4j health-check behavior against a real Neo4j Testcontainers database.
    /// </summary>
    public sealed class Neo4jHealthCheckTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jHealthCheckTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for the health probe.</param>
        public Neo4jHealthCheckTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Construction delegates shared container handling to the base class so this test can stay focused on health behavior.
        }

        /// <summary>
        /// Confirms the health check reports healthy when a real Neo4j container accepts the lightweight query.
        /// </summary>
        /// <returns>A task that completes after the health check has executed against Neo4j.</returns>
        [Fact]
        public async Task CheckHealthAsyncReturnsHealthyForRunningNeo4jContainer()
        {
            // The service provider mirrors host composition and proves the health check can resolve all required infrastructure
            // dependencies without starting the Aspire AppHost.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());

            await using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);
            Neo4jHealthCheck healthCheck = serviceProvider.GetRequiredService<Neo4jHealthCheck>();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }
}
