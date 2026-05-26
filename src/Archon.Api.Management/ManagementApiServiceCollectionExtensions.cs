using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Validation;
using Archon.Application.Graph.Persistence;
using Archon.Application.Management;
using Archon.Application.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Archon.Api.Management
{
    /// <summary>
    /// Registers services required by the controlled management API module.
    /// </summary>
    public static class ManagementApiServiceCollectionExtensions
    {
        /// <summary>
        /// Adds controlled management application services and local fallback stores.
        /// </summary>
        /// <param name="services">The service collection used by an API host or endpoint test host.</param>
        /// <returns>The same service collection so callers can chain additional registrations.</returns>
        public static IServiceCollection AddArchonManagementApi(this IServiceCollection services)
        {
            // Default registrations provide a runnable local API while allowing host composition to replace stores with infrastructure adapters.
            ArgumentNullException.ThrowIfNull(services);
            services.AddLogging();
            services.TryAddSingleton<IArchitectureSnapshotWriter, InMemoryArchitectureSnapshotWriter>();
            services.TryAddSingleton<IExtractionRunHistory, InMemoryExtractionRunHistory>();
            services.TryAddSingleton<IRuleCatalogStore, InMemoryRuleCatalogStore>();
            services.TryAddSingleton<StartExtractionRequestValidator>();
            services.TryAddSingleton<IManagementOperationsService, ManagementOperationsService>();
            return services;
        }
    }
}
