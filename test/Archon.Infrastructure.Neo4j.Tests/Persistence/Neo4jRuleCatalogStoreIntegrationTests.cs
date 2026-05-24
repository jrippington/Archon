using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies versioned rule catalog persistence against a real Neo4j Testcontainers database.
    /// </summary>
    public sealed class Neo4jRuleCatalogStoreIntegrationTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jRuleCatalogStoreIntegrationTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for catalog persistence validation.</param>
        public Neo4jRuleCatalogStoreIntegrationTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps this test focused on rule catalog behavior rather than container lifecycle setup.
        }

        /// <summary>
        /// Confirms upserts are idempotent by code/version, preserve new versions, and do not delete omitted historical rules.
        /// </summary>
        /// <returns>A task that completes after the catalog has been written and queried from Neo4j.</returns>
        [Fact]
        public async Task UpsertRulesAsync_ShouldPersistVersionedRulesIdempotentlyAndNonDestructively()
        {
            // The integration test exercises the same infrastructure registration that hosts use, without running the Aspire AppHost.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IRuleCatalogStore store = serviceProvider.GetRequiredService<IRuleCatalogStore>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("rule catalog persistence test"));
            RuleCatalogEntry firstVersion = RuleCatalogTestEntryFactory.Create("ARCHON-NEO4J-RULE", "1.0.0", enabled: true);
            RuleCatalogEntry disabledVersion = RuleCatalogTestEntryFactory.Create("ARCHON-NEO4J-RULE", "1.1.0", enabled: false);

            RuleCatalogUpsertResult firstResult = await store.UpsertRulesAsync([firstVersion], CancellationToken.None);
            RuleCatalogUpsertResult idempotentResult = await store.UpsertRulesAsync([firstVersion], CancellationToken.None);
            RuleCatalogUpsertResult secondResult = await store.UpsertRulesAsync([disabledVersion], CancellationToken.None);
            RuleCatalogUpsertResult omittedResult = await store.UpsertRulesAsync([disabledVersion], CancellationToken.None);
            int totalRules = await ReadRuleCountAsync(driver);
            bool disabledPersisted = await ReadRuleEnabledAsync(driver, "ARCHON-NEO4J-RULE", "1.1.0");

            Assert.True(firstResult.Succeeded);
            Assert.True(idempotentResult.Succeeded);
            Assert.True(secondResult.Succeeded);
            Assert.True(omittedResult.Succeeded);
            Assert.Equal(2, totalRules);
            Assert.False(disabledPersisted);
        }

        /// <summary>
        /// Creates a service provider using production Neo4j infrastructure registrations and container-derived configuration.
        /// </summary>
        /// <returns>A service provider ready to resolve Neo4j infrastructure services for integration tests.</returns>
        private ServiceProvider CreateServiceProvider()
        {
            // The provider mirrors host composition while avoiding the Aspire AppHost, which must not run during automated validation.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        /// Reads the number of persisted Archon rule catalog nodes.
        /// </summary>
        /// <param name="driver">The Neo4j driver connected to the test database.</param>
        /// <returns>The number of persisted rule nodes.</returns>
        private static async Task<int> ReadRuleCountAsync(IDriver driver)
        {
            // Counting nodes by label verifies idempotency without depending on Neo4j internal identifiers.
            await using IAsyncSession session = driver.AsyncSession(builder => builder.WithDefaultAccessMode(AccessMode.Read));
            IResultCursor cursor = await session.RunAsync("MATCH (rule:ArchonRule) RETURN count(rule) AS ruleCount");
            IRecord record = await cursor.SingleAsync();
            return record["ruleCount"].As<int>();
        }

        /// <summary>
        /// Reads the enabled flag for one persisted rule version.
        /// </summary>
        /// <param name="driver">The Neo4j driver connected to the test database.</param>
        /// <param name="ruleCode">The rule code to query.</param>
        /// <param name="version">The rule version to query.</param>
        /// <returns>The persisted enabled flag.</returns>
        private static async Task<bool> ReadRuleEnabledAsync(IDriver driver, string ruleCode, string version)
        {
            // The disabled flag is a first-class persisted property because disabled rules remain catalog history but are skipped for evaluation.
            await using IAsyncSession session = driver.AsyncSession(builder => builder.WithDefaultAccessMode(AccessMode.Read));
            IResultCursor cursor = await session.RunAsync(
                "MATCH (rule:ArchonRule { ruleCode: $ruleCode, ruleVersion: $ruleVersion }) RETURN rule.enabled AS enabled",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["ruleCode"] = ruleCode,
                    ["ruleVersion"] = version
                });
            IRecord record = await cursor.SingleAsync();
            return record["enabled"].As<bool>();
        }
    }
}
