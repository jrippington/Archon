using Archon.Application.Metrics;
using Archon.Application.Rules;
using Archon.Application.Extraction.Runs;
using Archon.Infrastructure.Neo4j.Configuration;
using Archon.Infrastructure.Neo4j.Driver;
using Archon.Infrastructure.Neo4j.Health;
using Archon.Infrastructure.Neo4j.Persistence;
using Archon.Infrastructure.Neo4j.Recreation;
using Archon.Infrastructure.Neo4j.Schema;
using Archon.Application.Graph.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.DependencyInjection
{
    /// <summary>
    /// Provides dependency-injection registration helpers for the Neo4j infrastructure adapter.
    /// </summary>
    /// <remarks>
    /// Host projects call these extensions at the outer composition boundary. The registrations keep Neo4j driver types inside the
    /// infrastructure layer while allowing health checks and later persistence services to use the shared session provider.
    /// </remarks>
    public static class Neo4jServiceCollectionExtensions
    {
        /// <summary>
        /// Adds validated Neo4j configuration, driver lifecycle services, session creation, schema initialization, and the Neo4j health check.
        /// </summary>
        /// <param name="services">The service collection that receives Neo4j infrastructure registrations.</param>
        /// <param name="configuration">The application configuration containing the <see cref="Neo4jOptions.SectionName"/> section.</param>
        /// <returns>The same service collection so host composition can continue fluently.</returns>
        public static IServiceCollection AddArchonNeo4j(this IServiceCollection services, IConfiguration configuration)
        {
            // Registration is split into options, lifecycle services, and health checks so tests and hosts get the same behavior
            // regardless of whether configuration comes from Aspire, user secrets, environment variables, or in-memory sources.
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddOptions<Neo4jOptions>()
                .Bind(configuration.GetSection(Neo4jOptions.SectionName))
                .ValidateOnStart();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<Neo4jOptions>, Neo4jOptionsValidator>());
            services.TryAddSingleton<INeo4jDriverFactory, Neo4jDriverFactory>();
            services.TryAddSingleton(serviceProvider => serviceProvider.GetRequiredService<INeo4jDriverFactory>().CreateDriver());
            services.TryAddSingleton<INeo4jSessionProvider, Neo4jSessionProvider>();
            services.TryAddSingleton<Neo4jSchemaStatementCatalog>();
            services.TryAddSingleton<IArchitectureGraphInitializer, Neo4jGraphInitializer>();
            services.TryAddSingleton<IArchitectureGraphRecreator, Neo4jGraphRecreator>();
            services.TryAddSingleton<Neo4jSnapshotPersistenceMapper>();
            services.TryAddSingleton<Neo4jPersistenceStageLogger>();
            services.AddSingleton<IArchitectureSnapshotWriter, Neo4jArchitectureSnapshotWriter>();
            services.AddSingleton<ISnapshotLifecycleQuery, Neo4jSnapshotLifecycleQuery>();
            services.AddSingleton<ISnapshotDeletionStore, Neo4jSnapshotDeletionStore>();
            services.AddSingleton<IExtractionRunHistory, Neo4jExtractionRunHistory>();
            services.AddSingleton<IRuleCatalogStore, Neo4jRuleCatalogStore>();
            services.AddSingleton<IFindingStore, Neo4jFindingStore>();
            services.AddSingleton<IHotlistQueryStore, Neo4jHotlistQueryStore>();
            services.AddSingleton<IMetricQueryStore, Neo4jMetricQueryStore>();
            services.TryAddSingleton<Neo4jHealthCheck>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthCheck, Neo4jHealthCheck>());

            services.AddHealthChecks()
                .AddCheck<Neo4jHealthCheck>(Neo4jHealthCheck.Name, tags: new[] { "ready", "neo4j" });

            return services;
        }
    }
}
