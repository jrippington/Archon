using Archon.Infrastructure.Neo4j.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Testcontainers
{
    /// <summary>
    /// Provides shared configuration helpers for tests that execute against a real Neo4j Testcontainers database.
    /// </summary>
    /// <remarks>
    /// The base class keeps container-derived connection values in one place so individual integration tests can focus on behavior
    /// rather than repeating configuration binding setup.
    /// </remarks>
    public abstract class Neo4jIntegrationTestBase : IClassFixture<Neo4jContainerFixture>
    {
        private readonly Neo4jContainerFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jIntegrationTestBase"/> class.
        /// </summary>
        /// <param name="fixture">The shared Neo4j container fixture supplied by xUnit.</param>
        protected Neo4jIntegrationTestBase(Neo4jContainerFixture fixture)
        {
            // The fixture is shared by test classes using this base type so container startup remains scoped and reusable.
            _fixture = fixture;
        }

        /// <summary>
        /// Creates configuration values that point infrastructure registration at the running Neo4j container.
        /// </summary>
        /// <returns>An in-memory configuration root containing container connection settings.</returns>
        protected IConfiguration CreateNeo4jConfiguration()
        {
            // Testcontainers.Neo4j defaults to the standard Neo4j user and a known test password. The connection string supplies
            // the mapped host port, while the database name remains the standard local `neo4j` database.
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                [$"{Neo4jOptions.SectionName}:Uri"] = _fixture.ConnectionString,
                [$"{Neo4jOptions.SectionName}:Database"] = "neo4j",
                [$"{Neo4jOptions.SectionName}:Username"] = "neo4j",
                [$"{Neo4jOptions.SectionName}:Password"] = "neo4j_password",
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
