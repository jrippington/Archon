using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.Configuration;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Health;
using Archon.Infrastructure.Neo4j.Persistence;
using Archon.Infrastructure.Neo4j.Recreation;
using Archon.Infrastructure.Neo4j.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.DependencyInjection
{
    /// <summary>
    /// Verifies host-level Neo4j infrastructure composition without starting the Aspire AppHost or a Neo4j container.
    /// </summary>
    /// <remarks>
    /// These tests exercise the composition surface that host projects use: the application layer resolves ports, while Neo4j driver
    /// details remain registered behind infrastructure services. The tests intentionally avoid resolving <see cref="IDriver" /> so they
    /// do not need a live database and do not trigger external network work.
    /// </remarks>
    public sealed class Neo4jInfrastructureCompositionTests
    {
        /// <summary>
        /// Confirms the Neo4j infrastructure extension registers every WP003 application port and supporting infrastructure service.
        /// </summary>
        [Fact]
        public void AddArchonNeo4jRegistersPersistencePortsAndHealthCheck()
        {
            // Service registration is the host composition contract. Resolving non-driver services proves composition works without
            // starting Aspire, while leaving IDriver unresolved avoids opening an external connection in a static composition test.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateConfiguration());

            using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);

            Assert.IsType<Neo4jGraphInitializer>(serviceProvider.GetRequiredService<IArchitectureGraphInitializer>());
            Assert.IsType<Neo4jGraphRecreator>(serviceProvider.GetRequiredService<IArchitectureGraphRecreator>());
            Assert.IsType<Neo4jArchitectureSnapshotWriter>(serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>());
            Assert.IsType<Neo4jHealthCheck>(serviceProvider.GetRequiredService<Neo4jHealthCheck>());
            Assert.IsType<Neo4jSessionProvider>(serviceProvider.GetRequiredService<INeo4jSessionProvider>());
            Assert.NotNull(serviceProvider.GetRequiredService<Neo4jSchemaStatementCatalog>());
        }

        /// <summary>
        /// Creates in-memory Neo4j configuration sufficient for dependency-injection validation.
        /// </summary>
        /// <returns>An in-memory configuration root containing the required Neo4j settings.</returns>
        private static IConfiguration CreateConfiguration()
        {
            // The settings mirror host-provided configuration but use a non-routable local URI because the test never resolves IDriver.
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                [$"{Neo4jOptions.SectionName}:Uri"] = "bolt://localhost:7687",
                [$"{Neo4jOptions.SectionName}:Database"] = "neo4j",
                [$"{Neo4jOptions.SectionName}:Username"] = "neo4j",
                [$"{Neo4jOptions.SectionName}:Password"] = "composition_password",
                [$"{Neo4jOptions.SectionName}:ConnectionTimeout"] = "00:00:30",
                [$"{Neo4jOptions.SectionName}:MaxTransactionRetryTime"] = "00:00:30",
                [$"{Neo4jOptions.SectionName}:EncryptionMode"] = nameof(Neo4jEncryptionMode.Unencrypted)
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }
    }
}
