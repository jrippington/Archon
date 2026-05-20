using Testcontainers.Neo4j;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Testcontainers
{
    /// <summary>
    /// Owns the real Neo4j Testcontainers database used by WP003 integration health-check tests.
    /// </summary>
    /// <remarks>
    /// The fixture starts only a Neo4j container. It deliberately does not start the Aspire AppHost, because automated validation
    /// must not launch long-running orchestration processes for this work item.
    /// </remarks>
    public sealed class Neo4jContainerFixture : IAsyncLifetime
    {
        private readonly Neo4jContainer _container;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jContainerFixture"/> class.
        /// </summary>
        public Neo4jContainerFixture()
        {
            // The package default configuration supplies a local Neo4j image, credentials, mapped ports, and readiness behavior.
            _container = new Neo4jBuilder().Build();
        }

        /// <summary>
        /// Gets the Bolt connection string for the running Neo4j container.
        /// </summary>
        public string ConnectionString => _container.GetConnectionString();

        /// <summary>
        /// Starts the Neo4j container before tests execute.
        /// </summary>
        /// <returns>A task that completes after Testcontainers reports Neo4j is ready.</returns>
        public async Task InitializeAsync()
        {
            // Starting the container here keeps Docker work scoped to tests that opt into this fixture.
            await _container.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops and removes the Neo4j container after tests complete.
        /// </summary>
        /// <returns>A task that completes after Testcontainers disposes Docker resources.</returns>
        public async Task DisposeAsync()
        {
            // Testcontainers handles cleanup of the mapped ports and container resources created for the integration test.
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
