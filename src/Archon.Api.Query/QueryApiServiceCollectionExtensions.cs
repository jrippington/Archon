using Archon.Application.ArchitectureRules;
using Archon.Application.Cycles;
using Archon.Application.Dashboard;
using Archon.Application.Diff;
using Archon.Application.Evidence;
using Archon.Application.Facts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Hotspots;
using Archon.Application.Metrics;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Runtime;
using Archon.Application.Search;
using Archon.Application.Symbols;
using Archon.Application.Traversal;
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
            services.TryAddSingleton<IArchitectureSnapshotWriter, InMemoryArchitectureSnapshotWriter>();
            services.TryAddSingleton<IRuleCatalogStore, InMemoryRuleCatalogStore>();
            services.TryAddSingleton<IFindingStore, InMemoryFindingStore>();
            services.TryAddSingleton<IHotlistQueryStore, InMemoryHotlistQueryStore>();
            services.TryAddSingleton<IHotlistQueryService, HotlistQueryService>();
            services.TryAddSingleton<IMetricQueryStore, InMemoryMetricQueryStore>();
            services.TryAddSingleton<IMetricQueryService, MetricQueryService>();
            services.TryAddSingleton<ICycleQueryService, CycleQueryService>();
            services.TryAddSingleton<IHotspotQueryService, HotspotQueryService>();
            services.TryAddSingleton<IArchitectureRuleQueryService, ArchitectureRuleQueryService>();
            services.TryAddSingleton<ISnapshotDiffService, SnapshotDiffService>();
            services.TryAddSingleton<ISearchQueryService, SearchQueryService>();
            services.TryAddSingleton<IDashboardSummaryQueryService, DashboardSummaryQueryService>();
            services.TryAddSingleton<IProjectQueryService, ProjectQueryService>();
            services.TryAddSingleton<IGraphTraversalQueryService, GraphTraversalQueryService>();
            services.TryAddSingleton<ISymbolQueryService, SymbolQueryService>();
            services.TryAddSingleton<IRuntimeQueryService, RuntimeQueryService>();
            services.TryAddSingleton<IFactQueryService, FactQueryService>();
            services.TryAddSingleton<IEvidenceQueryService, EvidenceQueryService>();
            return services;
        }
    }
}
