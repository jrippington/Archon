using Archon.Application.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Archon.Api.Query
{
    /// <summary>
    /// Registers services required by the WP012 query API module.
    /// </summary>
    public static class QueryApiServiceCollectionExtensions
    {
        /// <summary>
        /// Adds application query services and default in-memory query adapters for rule catalog and hotlist endpoints.
        /// </summary>
        /// <param name="services">The service collection used by the API host or test host.</param>
        /// <returns>The same service collection so callers can chain additional registrations.</returns>
        public static IServiceCollection AddArchonQueryApi(this IServiceCollection services)
        {
            // Query services default to in-memory stores so tests and local hosts work without Neo4j; infrastructure registrations can override them.
            ArgumentNullException.ThrowIfNull(services);
            services.AddLogging();
            services.TryAddSingleton<IRuleCatalogStore, InMemoryRuleCatalogStore>();
            services.TryAddSingleton<IFindingStore, InMemoryFindingStore>();
            services.TryAddSingleton<IHotlistQueryStore, InMemoryHotlistQueryStore>();
            services.TryAddSingleton<IHotlistQueryService, HotlistQueryService>();
            return services;
        }
    }
}
