using System.Text.Json;
using Archon.Api.Query.Contracts;
using Archon.Application.ArchitectureRules;
using Archon.Application.Cycles;
using Archon.Application.Dashboard;
using Archon.Application.Diff;
using Archon.Application.Evidence;
using Archon.Application.Facts;
using Archon.Application.Hotspots;
using Archon.Application.Metrics;
using Archon.Application.Projects;
using Archon.Application.Rules;
using Archon.Application.Runtime;
using Archon.Application.Search;
using Archon.Application.Symbols;
using Archon.Application.Traversal;
using Archon.Domain.Graph.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Archon.Api.Query
{
    /// <summary>
    /// Maps WP012 rule catalog, hotlist, finding detail, finding history, and suppression endpoints.
    /// </summary>
    public static class QueryEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the controlled WP012 query API endpoints without exposing arbitrary graph query execution.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder that receives query routes.</param>
        /// <returns>The same endpoint route builder so callers can chain additional route mapping.</returns>
        public static IEndpointRouteBuilder MapArchonQueryApi(this IEndpointRouteBuilder endpoints)
        {
            // Routes intentionally avoid a common /api prefix and expose only controlled query shapes.
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGet("/dashboard-summary", GetDashboardSummaryAsync)
                .WithName("GetDashboardSummary")
                .WithTags("Dashboard")
                .WithSummary("Get dashboard summary data")
                .WithDescription("Returns one bounded dashboard summary envelope for a repository, optional solution, and selected or latest snapshot scope.")
                .Produces<QueryApiResponse<DashboardSummaryDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/projects", ListProjectsAsync)
                .WithName("ListProjects")
                .WithTags("Projects")
                .WithSummary("List projects in a selected architecture snapshot")
                .WithDescription("Returns a bounded project catalogue page with controlled search, filters, deterministic sorting, stable identities, and snapshot metadata.")
                .Produces<QueryPagedApiResponse<ProjectCatalogueItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/projects/detail", GetProjectByQueryAsync)
                .WithName("GetProjectByQuery")
                .WithTags("Projects")
                .WithSummary("Get project detail by query identity")
                .WithDescription("Returns project detail by projectStableKey or unambiguous projectName when stable keys contain slash characters that are safer to pass through query parameters.")
                .Produces<QueryApiResponse<ProjectDetailDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<ProjectDisambiguationResponse>(StatusCodes.Status409Conflict, "application/json")
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/projects/{*projectStableKey}", GetProjectByStableKeyAsync)
                .WithName("GetProjectByStableKey")
                .WithTags("Projects")
                .WithSummary("Get project detail by stable key")
                .WithDescription("Returns project detail by exact stable key while using repository, solution, and snapshot query parameters for scope.")
                .Produces<QueryApiResponse<ProjectDetailDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<ProjectDisambiguationResponse>(StatusCodes.Status409Conflict, "application/json")
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/dependencies/direct", ListDirectDependenciesAsync)
                .WithName("ListDirectDependencies")
                .WithTags("GraphTraversal")
                .WithSummary("List direct dependency edges for a graph node")
                .WithDescription("Returns depth-one outgoing dependency-like edges for one stable node identity using bounded traversal response metadata.")
                .Produces<QueryApiResponse<GraphTraversalResponseDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/dependents/direct", ListDirectDependentsAsync)
                .WithName("ListDirectDependents")
                .WithTags("GraphTraversal")
                .WithSummary("List direct dependent edges for a graph node")
                .WithDescription("Returns depth-one incoming dependency-like edges for one stable node identity using bounded traversal response metadata.")
                .Produces<QueryApiResponse<GraphTraversalResponseDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/dependencies/transitive", ListTransitiveDependenciesAsync)
                .WithName("ListTransitiveDependencies")
                .WithTags("GraphTraversal")
                .WithSummary("List bounded transitive dependency edges for a graph node")
                .WithDescription("Returns outgoing dependency-like edges reachable from one stable node identity within the requested depth and result limits.")
                .Produces<QueryApiResponse<GraphTraversalResponseDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/dependents/transitive", ListTransitiveDependentsAsync)
                .WithName("ListTransitiveDependents")
                .WithTags("GraphTraversal")
                .WithSummary("List bounded transitive dependent edges for a graph node")
                .WithDescription("Returns incoming dependency-like edges reachable from one stable node identity within the requested depth and result limits.")
                .Produces<QueryApiResponse<GraphTraversalResponseDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/dependency-path", GetDependencyPathAsync)
                .WithName("GetDependencyPath")
                .WithTags("GraphTraversal")
                .WithSummary("Find a bounded dependency path between graph nodes")
                .WithDescription("Returns a stable node and edge path when one exists, or a no-path/unavailable-data payload when path search cannot prove a relationship.")
                .Produces<QueryApiResponse<DependencyPathResponseDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/graph-neighbourhood", GetGraphNeighbourhoodAsync)
                .WithName("GetGraphNeighbourhood")
                .WithTags("GraphTraversal")
                .WithSummary("Get a bounded graph neighbourhood around one node")
                .WithDescription("Returns incoming, outgoing, or both-direction graph edges around one stable node identity with controlled depth, edge-kind, and result limits.")
                .Produces<QueryApiResponse<GraphTraversalResponseDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/symbols", SearchSymbolsAsync)
                .WithName("SearchSymbols")
                .WithTags("Symbols")
                .WithSummary("Search persisted semantic symbols")
                .WithDescription("Returns a bounded symbol page for one selected snapshot with controlled filters, deterministic sorting, stable identities, evidence references, and unknown metadata.")
                .Produces<QueryPagedApiResponse<SymbolSearchItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/symbols/detail", GetSymbolDetailAsync)
                .WithName("GetSymbolDetail")
                .WithTags("Symbols")
                .WithSummary("Get symbol detail")
                .WithDescription("Returns one symbol detail envelope by symbolStableKey or exact searchText when stable keys contain slash characters that are safer to pass through query parameters.")
                .Produces<QueryApiResponse<SymbolDetailDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/symbols/usages", ListSymbolUsagesAsync)
                .WithName("ListSymbolUsages")
                .WithTags("Symbols")
                .WithSummary("List symbol usages")
                .WithDescription("Returns a bounded usage page for one stable symbol identity, including reference or call evidence with safely bounded source snippet previews.")
                .Produces<QueryPagedApiResponse<SymbolUsageDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/runtime/endpoints", ListRuntimeEndpointsAsync)
                .WithName("ListRuntimeEndpoints")
                .WithTags("Runtime")
                .WithSummary("List runtime endpoints")
                .WithDescription("Returns a bounded endpoint page for one selected snapshot with controlled filters for method, route, project, controller or handler, authorization, evidence, and runtime dependencies.")
                .Produces<QueryPagedApiResponse<RuntimeEndpointDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/runtime/controllers", GetControllerOrHandlerAsync)
                .WithName("GetControllerOrHandler")
                .WithTags("Runtime")
                .WithSummary("Get controller or handler detail")
                .WithDescription("Returns one controller or handler detail envelope by stable key or exact name when facts are persisted separately from endpoint nodes.")
                .Produces<QueryApiResponse<ControllerHandlerDetailDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/runtime/entry-points", ListRuntimeEntryPointsAsync)
                .WithName("ListRuntimeEntryPoints")
                .WithTags("Runtime")
                .WithSummary("List runtime entry points")
                .WithDescription("Returns bounded API, worker, console, or service-host entry-point facts for one selected snapshot.")
                .Produces<QueryPagedApiResponse<RuntimeEntryPointDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/runtime/workers", ListWorkersAsync)
                .WithName("ListWorkers")
                .WithTags("Runtime")
                .WithSummary("List worker runtime facts")
                .WithDescription("Returns bounded worker, hosted-service, background-service, queue/topic consumer, and scheduled-job facts for one selected snapshot.")
                .Produces<QueryPagedApiResponse<WorkerDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/data-access", ListDataAccessFactsAsync)
                .WithName("ListDataAccessFacts")
                .WithTags("Facts")
                .WithSummary("List data-access architecture facts")
                .WithDescription("Returns bounded LINQ to SQL, Entity Framework, ADO.NET, typed DataSet, raw SQL, stored procedure, entity, table, and usage-site facts for one selected snapshot.")
                .Produces<QueryPagedApiResponse<DataAccessFactDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/configuration", ListConfigurationUsageAsync)
                .WithName("ListConfigurationUsage")
                .WithTags("Facts")
                .WithSummary("List secret-safe configuration usage facts")
                .WithDescription("Returns bounded configuration key usage metadata for one selected snapshot without exposing values, connection strings, credentials, tokens, or other secrets.")
                .Produces<QueryPagedApiResponse<ConfigurationUsageDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/integrations", ListIntegrationFactsAsync)
                .WithName("ListIntegrationFacts")
                .WithTags("Facts")
                .WithSummary("List secret-safe external integration facts")
                .WithDescription("Returns bounded external service, queue, topic, protocol, client type, safe host, and configuration-key metadata without exposing credentials or connection strings.")
                .Produces<QueryPagedApiResponse<IntegrationFactDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/ui-technologies", ListUiTechnologyFactsAsync)
                .WithName("ListUiTechnologyFacts")
                .WithTags("Facts")
                .WithSummary("List backend UI-technology facts")
                .WithDescription("Returns bounded Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia graph facts as API data without introducing Discovery UI assets.")
                .Produces<QueryPagedApiResponse<UiTechnologyFactDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/evidence/detail", GetEvidenceDetailAsync)
                .WithName("GetEvidenceDetail")
                .WithTags("Evidence")
                .WithSummary("Get evidence detail by stable key")
                .WithDescription("Returns one bounded evidence detail envelope with file path, line range, symbol, snippet preview, related findings/rules, confidence, classification, unknown reason, and secret-safe metadata.")
                .Produces<QueryApiResponse<EvidenceDetailDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/evidence/related", ListRelatedEvidenceAsync)
                .WithName("ListRelatedEvidence")
                .WithTags("Evidence")
                .WithSummary("List evidence related to a graph record")
                .WithDescription("Returns bounded evidence detail rows related to a node, edge, finding, metric, or rule stable identity without expanding source beyond persisted previews.")
                .Produces<QueryPagedApiResponse<EvidenceDetailDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/rules", ListRulesAsync)
                .WithName("ListRules")
                .WithTags("Rules")
                .WithSummary("List persisted rule catalog entries")
                .WithDescription("Returns a bounded, deterministically ordered page of persisted rule catalog entries using controlled filters only.")
                .Produces<PagedApiResponse<RuleCatalogItemDto>>(StatusCodes.Status200OK, "application/json");

            endpoints.MapGet("/rules/{ruleCode}/{version}", GetRuleAsync)
                .WithName("GetRule")
                .WithTags("Rules")
                .WithSummary("Get one persisted rule catalog entry")
                .WithDescription("Returns the stable detail DTO for one exact rule code and version without exposing unrestricted graph access.")
                .Produces<RuleDetailDto>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/hotlist", ListHotlistAsync)
                .WithName("ListHotlist")
                .WithTags("Hotlist")
                .WithSummary("List persisted findings")
                .WithDescription("Returns a bounded, deterministically ordered page of persisted findings using controlled snapshot, critical-only, legacy data-access, out-of-support, security-sensitive, framework-only, project, technology, severity, status, rule-code, and affected-node filters.")
                .Produces<PagedApiResponse<HotlistItemDto>>(StatusCodes.Status200OK, "application/json");

            endpoints.MapGet("/snapshots/{snapshotStableKey}/metrics", ListSnapshotMetricsAsync)
                .WithName("ListSnapshotMetrics")
                .WithTags("Metrics")
                .WithSummary("List persisted snapshot metrics")
                .WithDescription("Returns a bounded, deterministically ordered page of persisted metrics for one snapshot using stable public identities and optional node-target filtering.")
                .Produces<PagedApiResponse<MetricItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/snapshot-metrics", ListSnapshotMetricsByQueryAsync)
                .WithName("ListSnapshotMetricsByQuery")
                .WithTags("Metrics")
                .WithSummary("List persisted snapshot metrics by query stable key")
                .WithDescription("Returns snapshot metrics when stable keys contain slash characters that are safer to pass through query parameters.")
                .Produces<PagedApiResponse<MetricItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/snapshot-cycles", ListSnapshotCyclesAsync)
                .WithName("ListSnapshotCycles")
                .WithTags("Cycles")
                .WithSummary("List detected dependency cycles by query stable key")
                .WithDescription("Returns a bounded, deterministically ordered page of dependency cycles for one snapshot using stable public identities and optional node participation filtering.")
                .Produces<PagedApiResponse<CycleItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/snapshot-hotspots", ListSnapshotHotspotsAsync)
                .WithName("ListSnapshotHotspots")
                .WithTags("Hotspots")
                .WithSummary("List detected architecture hotspots by query stable key")
                .WithDescription("Returns a bounded, deterministically ordered page of architecture hotspots for one snapshot using stable public identities and optional category or target filtering.")
                .Produces<PagedApiResponse<HotspotItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/snapshot-architecture-rules", ListSnapshotArchitectureRulesAsync)
                .WithName("ListSnapshotArchitectureRules")
                .WithTags("ArchitectureRules")
                .WithSummary("List evaluated architecture-rule results by query stable key")
                .WithDescription("Returns a bounded, deterministically ordered page of architecture-rule results for one snapshot using stable public identities and optional category, status, or target filtering.")
                .Produces<PagedApiResponse<ArchitectureRuleItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/snapshot-diff", GetSnapshotDiffAsync)
                .WithName("GetSnapshotDiff")
                .WithTags("SnapshotDiff")
                .WithSummary("Compare two architecture snapshots by stable keys and fingerprints")
                .WithDescription("Returns deterministic snapshot drift across nodes, edges, findings, and metrics with controlled filters, summary counts, and truncation metadata.")
                .Produces<SnapshotDiffResult>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/snapshot-diff/latest", GetLatestSnapshotDiffAsync)
                .WithName("GetLatestSnapshotDiff")
                .WithTags("SnapshotDiff")
                .WithSummary("Compare the latest architecture snapshot with its previous comparable snapshot")
                .WithDescription("Resolves the two newest comparable snapshots inside a repository and optional solution scope, then returns deterministic drift across nodes, edges, findings, and metrics.")
                .Produces<SnapshotDiffResult>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest);

            endpoints.MapGet("/search", SearchAcrossDomainsAsync)
                .WithName("SearchAcrossDomains")
                .WithTags("Search")
                .WithSummary("Search supported architecture records in a selected snapshot")
                .WithDescription("Returns bounded project, symbol, runtime, fact, evidence, finding, and metric search results with stable identities and deterministic follow-up affordances for API and future MCP clients.")
                .Produces<QueryPagedApiResponse<SearchResultItemDto>>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .Produces<QueryErrorResponse>(StatusCodes.Status500InternalServerError, "application/json");

            endpoints.MapGet("/findings/detail", GetFindingByQueryAsync)
                .WithName("GetFindingByQuery")
                .WithTags("Findings")
                .WithSummary("Get one persisted finding by query parameters")
                .WithDescription("Returns controlled finding detail when stable keys contain slash characters that are safer to pass through query parameters.")
                .Produces<FindingDetailDto>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/findings/{snapshotStableKey}/{*findingStableKey}", GetFindingAsync)
                .WithName("GetFinding")
                .WithTags("Findings")
                .WithSummary("Get one persisted finding")
                .WithDescription("Returns controlled finding detail and evidence references by snapshot stable key and finding stable key.")
                .Produces<FindingDetailDto>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/finding-history", GetFindingHistoryByQueryAsync)
                .WithName("GetFindingHistoryByQuery")
                .WithTags("Findings")
                .WithSummary("Get finding history by query parameter")
                .WithDescription("Returns finding history when history keys contain slash characters that are safer to pass through query parameters.")
                .Produces<FindingHistoryDto>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/findings/history/{*historyKey}", GetFindingHistoryAsync)
                .WithName("GetFindingHistory")
                .WithTags("Findings")
                .WithSummary("Get finding history")
                .WithDescription("Returns first-seen, latest-seen, and historical finding records for one deterministic finding history key.")
                .Produces<FindingHistoryDto>(StatusCodes.Status200OK, "application/json")
                .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/findings/suppressions", SuppressFindingAsync)
                .WithName("SuppressFinding")
                .WithTags("Findings")
                .WithSummary("Suppress a finding history target")
                .WithDescription("Validates and persists a suppression overlay for a finding history key, rule identity, and primary node identity without deleting findings.")
                .Accepts<SuppressFindingApiRequest>("application/json")
                .Produces<SuppressFindingApiResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status500InternalServerError);

            return endpoints;
        }

        /// <summary>
        /// Handles GET /dashboard-summary by returning one common query envelope for the selected dashboard scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="service">The application query service that owns dashboard summary behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the dashboard summary envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> GetDashboardSummaryAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            IDashboardSummaryQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint binds only stable selector values and delegates all graph interpretation to the application layer.
            try
            {
                DashboardSnapshotSelector selector = new(repositoryStableKey, solutionStableKey, snapshotStableKey);
                DashboardSummaryResult result = await service.GetDashboardSummaryAsync(selector, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToDashboardValidationProblem(result);
                }

                QueryApiResponse<DashboardSummaryDto> response = ToDashboardResponse(result.Summary!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query for resolved dashboard summary with {WarningCount} warnings and {UnknownCount} unknowns.",
                    "dashboard-summary",
                    response.Warnings.Count,
                    response.Unknowns.Count);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Dashboard summary query failed with a safe public error response.");
                QueryErrorResponse error = new("DashboardSummaryFailed", "Dashboard summary could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /projects by returning a bounded project catalogue envelope for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="search">The optional search text applied to public project identity fields.</param>
        /// <param name="language">The optional exact language filter.</param>
        /// <param name="projectType">The optional exact project type filter.</param>
        /// <param name="targetFramework">The optional exact target framework filter.</param>
        /// <param name="applicationType">The optional exact application type filter.</param>
        /// <param name="hasDataAccess">The optional data-access indicator filter.</param>
        /// <param name="hasRisk">The optional risk indicator filter.</param>
        /// <param name="sort">The optional deterministic sort field.</param>
        /// <param name="descending">The optional descending sort flag.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns project catalogue behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the project catalogue envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListProjectsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? search,
            string? language,
            string? projectType,
            string? targetFramework,
            string? applicationType,
            bool? hasDataAccess,
            bool? hasRisk,
            string? sort,
            bool? descending,
            int? skip,
            int? take,
            IProjectQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint binds only stable scope values and fixed filters before delegating graph interpretation to the application layer.
            try
            {
                ProjectCatalogueQuery query = new(repositoryStableKey, solutionStableKey, snapshotStableKey, search, language, projectType, targetFramework, applicationType, hasDataAccess, hasRisk, sort, descending, skip, take);
                ProjectCatalogueResult result = await service.ListProjectsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToProjectValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<ProjectCatalogueItemDto> response = ToProjectPagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching projects, skip {Skip}, and take {Take}.",
                    "projects",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (ArgumentException exception)
            {
                return QueryValidationProblemFactory.FromArgumentException(exception, "projects");
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Project catalogue query failed with a safe public error response.");
                QueryErrorResponse error = new("ProjectCatalogueFailed", "Project catalogue could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /projects/{projectStableKey} by returning one project detail envelope for an exact stable key.
        /// </summary>
        /// <param name="projectStableKey">The route project stable key.</param>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="service">The application query service that owns project detail behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing project detail, a validation problem, a conflict response, or a safe server error response.</returns>
        private static Task<IResult> GetProjectByStableKeyAsync(
            string? projectStableKey,
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            IProjectQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Route-based stable-key lookup keeps project identity explicit while query-string scope controls snapshot selection.
            ProjectDetailQuery query = new(repositoryStableKey, solutionStableKey, snapshotStableKey, projectStableKey, projectName: null);
            return ExecuteProjectDetailAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Handles GET /projects/detail by returning one project detail envelope for a stable key or unambiguous project name.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="projectStableKey">The optional exact project stable key.</param>
        /// <param name="projectName">The optional project display name for unambiguous-name lookup.</param>
        /// <param name="service">The application query service that owns project detail behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing project detail, a validation problem, a conflict response, or a safe server error response.</returns>
        private static Task<IResult> GetProjectByQueryAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? projectStableKey,
            string? projectName,
            IProjectQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys that include slash separators and also supports name lookup.
            ProjectDetailQuery query = new(repositoryStableKey, solutionStableKey, snapshotStableKey, projectStableKey, projectName);
            return ExecuteProjectDetailAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Executes project detail query handling for both route and query-string project lookup endpoints.
        /// </summary>
        /// <param name="query">The normalized project detail query.</param>
        /// <param name="service">The application query service that owns project detail behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing project detail, a validation problem, a conflict response, or a safe server error response.</returns>
        private static async Task<IResult> ExecuteProjectDetailAsync(ProjectDetailQuery query, IProjectQueryService service, ILoggerFactory loggerFactory, HttpContext httpContext, CancellationToken cancellationToken)
        {
            // Shared execution keeps route and query-string detail endpoints behaviorally identical after binding.
            try
            {
                ProjectDetailResult result = await service.GetProjectAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    if (result.DisambiguationOptions.Count > 0)
                    {
                        ProjectQueryValidationError error = result.ValidationErrors.First();
                        ProjectDisambiguationResponse response = new(error.Code, error.Message, result.DisambiguationOptions, httpContext.TraceIdentifier);
                        return Results.Json(response, statusCode: StatusCodes.Status409Conflict);
                    }

                    return ToProjectValidationProblem(result.ValidationErrors);
                }

                QueryApiResponse<ProjectDetailDto> detailResponse = ToProjectDetailResponse(result.Detail!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {EvidenceCount} evidence references and {UnknownCount} unknowns.",
                    "project-detail",
                    detailResponse.Data.Evidence.Count,
                    detailResponse.Unknowns.Count);
                return Results.Ok(detailResponse);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Project detail query failed with a safe public error response.");
                QueryErrorResponse error = new("ProjectDetailFailed", "Project detail could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /evidence/detail by returning one evidence detail envelope for an exact evidence stable key.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="evidenceStableKey">The required evidence stable key.</param>
        /// <param name="service">The application query service that owns evidence detail behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing evidence detail, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> GetEvidenceDetailAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? evidenceStableKey,
            IEvidenceQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys that include slash separators.
            try
            {
                EvidenceDetailQuery query = new(new EvidenceSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey), evidenceStableKey);
                EvidenceDetailResult result = await service.GetEvidenceAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToEvidenceValidationProblem(result.ValidationErrors);
                }

                QueryApiResponse<EvidenceDetailDto> response = ToEvidenceDetailResponse(result.Detail!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {RelatedRecordCount} related evidence records and {UnknownCount} unknowns.",
                    "evidence-detail",
                    response.Data.RelatedRecords.Count,
                    response.Unknowns.Count);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Evidence detail query failed with a safe public error response.");
                QueryErrorResponse error = new("EvidenceDetailFailed", "Evidence detail could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /evidence/related by returning a bounded evidence page for a related stable record.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="relatedStableKey">The required related node, edge, finding, metric, or rule stable key.</param>
        /// <param name="relatedKind">The optional related-record kind hint.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns evidence relationship behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing a related-evidence page, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListRelatedEvidenceAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? relatedStableKey,
            string? relatedKind,
            int? skip,
            int? take,
            IEvidenceQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Related-evidence lookup is bounded and follows only explicit stable evidence relationships.
            try
            {
                RelatedEvidenceQuery query = new(new EvidenceSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey), relatedStableKey, relatedKind, skip ?? 0, take ?? EvidenceQueryLimits.DefaultTake);
                RelatedEvidenceResult result = await service.ListRelatedEvidenceAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToEvidenceValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<EvidenceDetailDto> response = ToEvidencePagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} related evidence records, skip {Skip}, and take {Take}.",
                    "related-evidence",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Related evidence query failed with a safe public error response.");
                QueryErrorResponse error = new("RelatedEvidenceFailed", "Related evidence could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /dependencies/direct by returning depth-one outgoing dependency traversal data.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="nodeStableKey">The required traversal start node stable key.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="take">The optional maximum number of edge records to return.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK traversal envelope, a validation problem, or a safe server error response.</returns>
        private static Task<IResult> ListDirectDependenciesAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? nodeStableKey,
            string[]? edgeKinds,
            int? take,
            IGraphTraversalQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Direct dependency reads are depth-one outgoing traversal over the approved dependency-like edge set.
            GraphTraversalQuery query = CreateTraversalQuery(repositoryStableKey, solutionStableKey, snapshotStableKey, nodeStableKey, "Outgoing", depth: 1, edgeKinds, take, "DirectDependencies");
            return ExecuteGraphTraversalAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Handles GET /dependents/direct by returning depth-one incoming dependency traversal data.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="nodeStableKey">The required traversal start node stable key.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="take">The optional maximum number of edge records to return.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK traversal envelope, a validation problem, or a safe server error response.</returns>
        private static Task<IResult> ListDirectDependentsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? nodeStableKey,
            string[]? edgeKinds,
            int? take,
            IGraphTraversalQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Direct dependent reads are depth-one incoming traversal so callers can inspect immediate consumers of a graph node.
            GraphTraversalQuery query = CreateTraversalQuery(repositoryStableKey, solutionStableKey, snapshotStableKey, nodeStableKey, "Incoming", depth: 1, edgeKinds, take, "DirectDependents");
            return ExecuteGraphTraversalAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Handles GET /dependencies/transitive by returning bounded outgoing dependency traversal data.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="nodeStableKey">The required traversal start node stable key.</param>
        /// <param name="depth">The optional maximum traversal depth.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="take">The optional maximum number of edge records to return.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK traversal envelope, a validation problem, or a safe server error response.</returns>
        private static Task<IResult> ListTransitiveDependenciesAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? nodeStableKey,
            int? depth,
            string[]? edgeKinds,
            int? take,
            IGraphTraversalQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Transitive dependency reads walk outgoing edges up to the caller-supplied bounded depth.
            GraphTraversalQuery query = CreateTraversalQuery(repositoryStableKey, solutionStableKey, snapshotStableKey, nodeStableKey, "Outgoing", depth ?? GraphTraversalLimits.DefaultTransitiveDepth, edgeKinds, take, "TransitiveDependencies");
            return ExecuteGraphTraversalAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Handles GET /dependents/transitive by returning bounded incoming dependency traversal data.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="nodeStableKey">The required traversal start node stable key.</param>
        /// <param name="depth">The optional maximum traversal depth.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="take">The optional maximum number of edge records to return.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK traversal envelope, a validation problem, or a safe server error response.</returns>
        private static Task<IResult> ListTransitiveDependentsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? nodeStableKey,
            int? depth,
            string[]? edgeKinds,
            int? take,
            IGraphTraversalQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Transitive dependent reads walk incoming edges up to the caller-supplied bounded depth.
            GraphTraversalQuery query = CreateTraversalQuery(repositoryStableKey, solutionStableKey, snapshotStableKey, nodeStableKey, "Incoming", depth ?? GraphTraversalLimits.DefaultTransitiveDepth, edgeKinds, take, "TransitiveDependents");
            return ExecuteGraphTraversalAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Handles GET /graph-neighbourhood by returning bounded graph edges around one stable node.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="nodeStableKey">The required traversal start node stable key.</param>
        /// <param name="direction">The optional direction filter: Outgoing, Incoming, or Both.</param>
        /// <param name="depth">The optional maximum traversal depth, defaulting to one hop.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="take">The optional maximum number of edge records to return.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK traversal envelope, a validation problem, or a safe server error response.</returns>
        private static Task<IResult> GetGraphNeighbourhoodAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? nodeStableKey,
            string? direction,
            int? depth,
            string[]? edgeKinds,
            int? take,
            IGraphTraversalQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Neighbourhood reads default to depth one and both directions, while still applying the same result-size limits as dependency traversal.
            GraphTraversalQuery query = CreateTraversalQuery(repositoryStableKey, solutionStableKey, snapshotStableKey, nodeStableKey, direction ?? "Both", depth ?? GraphTraversalLimits.DefaultNeighbourhoodDepth, edgeKinds, take, "GraphNeighbourhood");
            return ExecuteGraphTraversalAsync(query, service, loggerFactory, httpContext, cancellationToken);
        }

        /// <summary>
        /// Handles GET /dependency-path by returning a bounded dependency path payload.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="sourceNodeStableKey">The required source node stable key.</param>
        /// <param name="targetNodeStableKey">The required target node stable key.</param>
        /// <param name="depth">The optional maximum path-search depth.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK path envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> GetDependencyPathAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? sourceNodeStableKey,
            string? targetNodeStableKey,
            int? depth,
            string[]? edgeKinds,
            IGraphTraversalQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Path lookup searches outgoing dependency direction and reports no-path as data rather than a 404.
            try
            {
                DependencyPathQuery query = new(
                    new GraphTraversalSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey),
                    sourceNodeStableKey,
                    targetNodeStableKey,
                    depth ?? GraphTraversalLimits.DefaultTransitiveDepth,
                    edgeKinds ?? []);
                DependencyPathResult result = await service.GetDependencyPathAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToGraphTraversalValidationProblem(result.ValidationErrors);
                }

                QueryApiResponse<DependencyPathResponseDto> response = ToDependencyPathResponse(result.Response!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with path found {PathFound}, unavailable {Unavailable}, and {EdgeCount} path edges.",
                    "dependency-path",
                    response.Data.PathFound,
                    response.Data.Unavailable,
                    response.Data.Edges.Count);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Dependency path query failed with a safe public error response.");
                QueryErrorResponse error = new("DependencyPathFailed", "Dependency path could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Creates a normalized traversal query from HTTP-bound parameter values.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="nodeStableKey">The required traversal start node stable key.</param>
        /// <param name="direction">The traversal direction to apply.</param>
        /// <param name="depth">The maximum traversal depth to apply.</param>
        /// <param name="edgeKinds">The optional comma-separated or repeated edge-kind filters.</param>
        /// <param name="take">The optional maximum number of edge records to return.</param>
        /// <param name="mode">The public traversal mode used for response metadata.</param>
        /// <returns>The application traversal query.</returns>
        private static GraphTraversalQuery CreateTraversalQuery(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey, string? nodeStableKey, string direction, int depth, string[]? edgeKinds, int? take, string mode)
        {
            // Query construction applies endpoint defaults but leaves validation to the application layer for consistent error codes.
            return new GraphTraversalQuery(
                new GraphTraversalSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey),
                nodeStableKey,
                direction,
                depth,
                edgeKinds ?? [],
                take ?? GraphTraversalLimits.DefaultResultLimit,
                mode);
        }

        /// <summary>
        /// Executes shared traversal endpoint behavior for dependency, dependent, and neighbourhood routes.
        /// </summary>
        /// <param name="query">The application traversal query.</param>
        /// <param name="service">The application query service that owns traversal behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK traversal envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ExecuteGraphTraversalAsync(GraphTraversalQuery query, IGraphTraversalQueryService service, ILoggerFactory loggerFactory, HttpContext httpContext, CancellationToken cancellationToken)
        {
            // Shared execution keeps all traversal endpoints behaviorally consistent after endpoint-specific defaults are bound.
            try
            {
                GraphTraversalResult result = await service.TraverseAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToGraphTraversalValidationProblem(result.ValidationErrors);
                }

                QueryApiResponse<GraphTraversalResponseDto> response = ToGraphTraversalResponse(result.Response!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {EdgeCount} edges, depth {Depth}, and truncation status {Truncated}.",
                    "graph-traversal",
                    response.Data.Edges.Count,
                    response.Data.Depth,
                    response.Truncation.Truncated);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Graph traversal query failed with a safe public error response.");
                QueryErrorResponse error = new("GraphTraversalFailed", "Graph traversal could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /symbols by returning a bounded semantic symbol page for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="searchText">The optional text matched against public symbol identity fields.</param>
        /// <param name="projectStableKey">The optional owning project stable-key filter.</param>
        /// <param name="kind">The optional exact symbol kind filter.</param>
        /// <param name="namespaceName">The optional exact namespace filter bound from the namespace query parameter.</param>
        /// <param name="containingType">The optional exact containing type filter.</param>
        /// <param name="language">The optional exact language filter.</param>
        /// <param name="sort">The optional deterministic sort field.</param>
        /// <param name="descending">The optional descending sort flag.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns symbol behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the symbol page envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> SearchSymbolsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? searchText,
            string? projectStableKey,
            string? kind,
            string? namespaceName,
            string? containingType,
            string? language,
            string? sort,
            bool? descending,
            int? skip,
            int? take,
            ISymbolQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint accepts only fixed filters and delegates semantic graph interpretation to the application layer.
            try
            {
                SymbolSearchQuery query = new(
                    new SymbolSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey),
                    searchText,
                    projectStableKey,
                    kind,
                    namespaceName,
                    containingType,
                    language,
                    sort,
                    descending ?? false,
                    skip ?? 0,
                    take ?? SymbolQueryLimits.DefaultTake);
                SymbolSearchResult result = await service.SearchSymbolsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToSymbolValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<SymbolSearchItemDto> response = ToSymbolPagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching symbols, skip {Skip}, and take {Take}.",
                    "symbols",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Symbol search query failed with a safe public error response.");
                QueryErrorResponse error = new("SymbolSearchFailed", "Symbol search could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /symbols/detail by returning one semantic symbol detail envelope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="symbolStableKey">The optional exact symbol stable key.</param>
        /// <param name="searchText">The optional exact symbol search text.</param>
        /// <param name="service">The application query service that owns symbol behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing symbol detail, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> GetSymbolDetailAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? symbolStableKey,
            string? searchText,
            ISymbolQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Query-string identity avoids path decoding ambiguity for stable keys that include slash separators.
            try
            {
                SymbolDetailQuery query = new(new SymbolSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey), symbolStableKey, searchText);
                SymbolDetailResult result = await service.GetSymbolAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToSymbolValidationProblem(result.ValidationErrors);
                }

                QueryApiResponse<SymbolDetailDto> response = ToSymbolDetailResponse(result.Detail!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {EvidenceCount} evidence references, {RelationshipCount} relationships, and {UnknownCount} unknowns.",
                    "symbol-detail",
                    response.Data.Evidence.Count,
                    response.Data.Relationships.Count,
                    response.Unknowns.Count);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Symbol detail query failed with a safe public error response.");
                QueryErrorResponse error = new("SymbolDetailFailed", "Symbol detail could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /symbols/usages by returning a bounded semantic usage page for one symbol.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="symbolStableKey">The required symbol stable key whose usages should be listed.</param>
        /// <param name="direction">The optional usage direction, Incoming or Outgoing.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns symbol behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing symbol usages, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListSymbolUsagesAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? symbolStableKey,
            string? direction,
            int? skip,
            int? take,
            ISymbolQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Usage reads are direction-bound and page-bound so callers can inspect references without unbounded graph traversal.
            try
            {
                SymbolUsageQuery query = new(
                    new SymbolSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey),
                    symbolStableKey,
                    direction,
                    skip ?? 0,
                    take ?? SymbolQueryLimits.DefaultTake);
                SymbolUsageResult result = await service.ListSymbolUsagesAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToSymbolValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<SymbolUsageDto> response = ToSymbolUsageResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching symbol usages, skip {Skip}, and take {Take}.",
                    "symbol-usages",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Symbol usage query failed with a safe public error response.");
                QueryErrorResponse error = new("SymbolUsageFailed", "Symbol usages could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /runtime/endpoints by returning a bounded runtime endpoint page for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="httpMethod">The optional exact HTTP method filter.</param>
        /// <param name="route">The optional route-template search filter.</param>
        /// <param name="projectStableKey">The optional owning project stable-key filter.</param>
        /// <param name="controllerOrHandler">The optional controller, handler, action, or method search filter.</param>
        /// <param name="authorization">The optional authorization attribute search filter.</param>
        /// <param name="sort">The optional deterministic sort field.</param>
        /// <param name="descending">The optional descending sort flag.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns runtime query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing runtime endpoints, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListRuntimeEndpointsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? httpMethod,
            string? route,
            string? projectStableKey,
            string? controllerOrHandler,
            string? authorization,
            string? sort,
            bool? descending,
            int? skip,
            int? take,
            IRuntimeQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Runtime endpoint lookup accepts only fixed filters and delegates graph interpretation to the application layer.
            try
            {
                RuntimeEndpointQuery query = new(
                    new RuntimeSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey),
                    httpMethod,
                    route,
                    projectStableKey,
                    controllerOrHandler,
                    authorization,
                    sort,
                    descending ?? false,
                    skip ?? 0,
                    take ?? RuntimeQueryLimits.DefaultTake);
                RuntimeEndpointResult result = await service.ListEndpointsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToRuntimeValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<RuntimeEndpointDto> response = ToRuntimePagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching runtime endpoints, skip {Skip}, and take {Take}.",
                    "runtime-endpoints",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Runtime endpoint query failed with a safe public error response.");
                QueryErrorResponse error = new("RuntimeEndpointQueryFailed", "Runtime endpoints could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /runtime/controllers by returning one controller or handler detail envelope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="stableKey">The optional controller or handler stable key.</param>
        /// <param name="name">The optional exact controller or handler name.</param>
        /// <param name="service">The application query service that owns runtime query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing controller or handler detail, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> GetControllerOrHandlerAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? stableKey,
            string? name,
            IRuntimeQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Query-string identity avoids path decoding ambiguity for stable keys that include slash separators.
            try
            {
                ControllerHandlerQuery query = new(new RuntimeSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey), stableKey, name);
                ControllerHandlerResult result = await service.GetControllerOrHandlerAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToRuntimeValidationProblem(result.ValidationErrors);
                }

                QueryApiResponse<ControllerHandlerDetailDto> response = ToRuntimeDetailResponse(result.Detail!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {EndpointCount} endpoints and {EvidenceCount} evidence references.",
                    "runtime-controllers",
                    response.Data.Endpoints.Count,
                    response.Data.Evidence.Count);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Controller or handler query failed with a safe public error response.");
                QueryErrorResponse error = new("ControllerHandlerQueryFailed", "Controller or handler detail could not be created.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /runtime/entry-points by returning a bounded runtime entry-point page for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="runtimeKind">The optional runtime kind filter.</param>
        /// <param name="projectStableKey">The optional owning project stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns runtime query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing runtime entry points, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListRuntimeEntryPointsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? runtimeKind,
            string? projectStableKey,
            int? skip,
            int? take,
            IRuntimeQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Entry-point queries reveal runtime host facts without exposing arbitrary graph traversal or UI assets.
            try
            {
                RuntimeEntryPointQuery query = new(new RuntimeSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey), runtimeKind, projectStableKey, skip ?? 0, take ?? RuntimeQueryLimits.DefaultTake);
                RuntimeEntryPointResult result = await service.ListEntryPointsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToRuntimeValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<RuntimeEntryPointDto> response = ToRuntimePagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching runtime entry points, skip {Skip}, and take {Take}.",
                    "runtime-entry-points",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Runtime entry-point query failed with a safe public error response.");
                QueryErrorResponse error = new("RuntimeEntryPointQueryFailed", "Runtime entry points could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /runtime/workers by returning a bounded worker page for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="projectStableKey">The optional owning project stable-key filter.</param>
        /// <param name="workerKind">The optional worker kind filter.</param>
        /// <param name="queueOrTopic">The optional queue or topic display-name search filter.</param>
        /// <param name="scheduledJob">The optional scheduled-job display-name search filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns runtime query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing workers, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListWorkersAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? projectStableKey,
            string? workerKind,
            string? queueOrTopic,
            string? scheduledJob,
            int? skip,
            int? take,
            IRuntimeQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // Worker queries expose non-HTTP runtime behavior through fixed filters and bounded page sizes only.
            try
            {
                WorkerQuery query = new(new RuntimeSnapshotSelector(repositoryStableKey, solutionStableKey, snapshotStableKey), projectStableKey, workerKind, queueOrTopic, scheduledJob, skip ?? 0, take ?? RuntimeQueryLimits.DefaultTake);
                WorkerResult result = await service.ListWorkersAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToRuntimeValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<WorkerDto> response = ToWorkerPagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching workers, skip {Skip}, and take {Take}.",
                    "runtime-workers",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Worker query failed with a safe public error response.");
                QueryErrorResponse error = new("WorkerQueryFailed", "Workers could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /data-access by returning a bounded data-access fact envelope for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="family">The optional data-access family filter.</param>
        /// <param name="projectStableKey">The optional exact owning project stable-key filter.</param>
        /// <param name="usageSite">The optional usage-site text filter.</param>
        /// <param name="entity">The optional entity display-name or stable-key filter.</param>
        /// <param name="table">The optional table display-name or stable-key filter.</param>
        /// <param name="storedProcedure">The optional stored-procedure display-name or stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns fact-query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the data-access fact envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListDataAccessFactsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? family,
            string? projectStableKey,
            string? usageSite,
            string? entity,
            string? table,
            string? storedProcedure,
            int? skip,
            int? take,
            IFactQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint accepts only fixed filters and delegates graph interpretation and secret safety to the application layer.
            try
            {
                FactSnapshotSelector selector = new(repositoryStableKey, solutionStableKey, snapshotStableKey);
                DataAccessFactQuery query = new(selector, family, projectStableKey, usageSite, entity, table, storedProcedure, skip ?? 0, take ?? FactQueryLimits.DefaultTake);
                DataAccessFactResult result = await service.ListDataAccessFactsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToFactValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<DataAccessFactDto> response = ToFactPagedResponse(result.Page!, result.Context!, httpContext);
                LogPageResult(loggerFactory, "data-access", response.TotalCount, response.Skip, response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Data-access fact query failed with a safe public error response.");
                QueryErrorResponse error = new("DataAccessFactQueryFailed", "Data-access facts could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /configuration by returning a bounded secret-safe configuration usage envelope for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="configurationKey">The optional configuration-key text filter.</param>
        /// <param name="projectStableKey">The optional exact owning project stable-key filter.</param>
        /// <param name="consumerStableKey">The optional exact consumer node stable-key filter.</param>
        /// <param name="provider">The optional configuration provider filter.</param>
        /// <param name="environment">The optional environment-name filter.</param>
        /// <param name="sourceFile">The optional source-file path filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns fact-query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the configuration usage envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListConfigurationUsageAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? configurationKey,
            string? projectStableKey,
            string? consumerStableKey,
            string? provider,
            string? environment,
            string? sourceFile,
            int? skip,
            int? take,
            IFactQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint never binds configuration values; only safe key metadata and fixed filters enter the application layer.
            try
            {
                FactSnapshotSelector selector = new(repositoryStableKey, solutionStableKey, snapshotStableKey);
                ConfigurationUsageQuery query = new(selector, configurationKey, projectStableKey, consumerStableKey, provider, environment, sourceFile, skip ?? 0, take ?? FactQueryLimits.DefaultTake);
                ConfigurationUsageResult result = await service.ListConfigurationUsageAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToFactValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<ConfigurationUsageDto> response = ToFactPagedResponse(result.Page!, result.Context!, httpContext);
                LogPageResult(loggerFactory, "configuration", response.TotalCount, response.Skip, response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Configuration usage query failed with a safe public error response.");
                QueryErrorResponse error = new("ConfigurationUsageQueryFailed", "Configuration usage facts could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /integrations by returning a bounded secret-safe external integration envelope for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="projectStableKey">The optional exact owning project stable-key filter.</param>
        /// <param name="integrationKind">The optional integration kind filter.</param>
        /// <param name="endpointHost">The optional safe endpoint host or service-name filter.</param>
        /// <param name="protocol">The optional protocol filter.</param>
        /// <param name="clientType">The optional client type filter.</param>
        /// <param name="configurationKey">The optional safe configuration-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns fact-query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the integration fact envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListIntegrationFactsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? projectStableKey,
            string? integrationKind,
            string? endpointHost,
            string? protocol,
            string? clientType,
            string? configurationKey,
            int? skip,
            int? take,
            IFactQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint accepts only safe target filters while the service strips unsafe URL paths, credentials, and query strings.
            try
            {
                FactSnapshotSelector selector = new(repositoryStableKey, solutionStableKey, snapshotStableKey);
                IntegrationFactQuery query = new(selector, projectStableKey, integrationKind, endpointHost, protocol, clientType, configurationKey, skip ?? 0, take ?? FactQueryLimits.DefaultTake);
                IntegrationFactResult result = await service.ListIntegrationFactsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToFactValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<IntegrationFactDto> response = ToFactPagedResponse(result.Page!, result.Context!, httpContext);
                LogPageResult(loggerFactory, "integrations", response.TotalCount, response.Skip, response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Integration fact query failed with a safe public error response.");
                QueryErrorResponse error = new("IntegrationFactQueryFailed", "Integration facts could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /ui-technologies by returning a bounded backend UI-technology fact envelope for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="technology">The optional UI technology filter.</param>
        /// <param name="projectStableKey">The optional exact owning project stable-key filter.</param>
        /// <param name="route">The optional route or view path filter.</param>
        /// <param name="component">The optional component, page, view, control, or binding text filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns fact-query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the UI-technology fact envelope, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> ListUiTechnologyFactsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? technology,
            string? projectStableKey,
            string? route,
            string? component,
            int? skip,
            int? take,
            IFactQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint exposes backend UI graph facts only and does not add UI pages, components, or frontend assets.
            try
            {
                FactSnapshotSelector selector = new(repositoryStableKey, solutionStableKey, snapshotStableKey);
                UiTechnologyFactQuery query = new(selector, technology, projectStableKey, route, component, skip ?? 0, take ?? FactQueryLimits.DefaultTake);
                UiTechnologyFactResult result = await service.ListUiTechnologyFactsAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToFactValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<UiTechnologyFactDto> response = ToFactPagedResponse(result.Page!, result.Context!, httpContext);
                LogPageResult(loggerFactory, "ui-technologies", response.TotalCount, response.Skip, response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys or source metadata, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "UI-technology fact query failed with a safe public error response.");
                QueryErrorResponse error = new("UiTechnologyFactQueryFailed", "UI-technology facts could not be listed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Handles GET /snapshot-hotspots by returning a controlled hotspot page from query-string stable keys.
        /// </summary>
        /// <param name="snapshotStableKey">The required query-string snapshot stable-key filter.</param>
        /// <param name="targetStableKey">The optional exact hotspot target stable-key filter.</param>
        /// <param name="category">The optional exact hotspot category filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application hotspot query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the hotspot page, or a validation problem response for invalid query input.</returns>
        private static async Task<IResult> ListSnapshotHotspotsAsync(
            string? snapshotStableKey,
            string? targetStableKey,
            string? category,
            int? skip,
            int? take,
            IHotspotQueryService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys that include slash separators.
            try
            {
                HotspotQuery query = new(snapshotStableKey, targetStableKey, category, skip, take);
                PagedQueryResult<HotspotItemDto> result = await service.ListHotspotsAsync(query, cancellationToken).ConfigureAwait(false);
                LogPageResult(loggerFactory, "snapshot-hotspots", result.TotalCount, result.Skip, result.Take);
                return Results.Ok(ToPagedResponse(result));
            }
            catch (ArgumentException exception)
            {
                return QueryValidationProblemFactory.FromArgumentException(exception, "hotspots");
            }
        }

        /// <summary>
        /// Handles GET /snapshot-architecture-rules by returning a controlled architecture-rule result page from query-string stable keys.
        /// </summary>
        /// <param name="snapshotStableKey">The required query-string snapshot stable-key filter.</param>
        /// <param name="category">The optional exact rule category filter.</param>
        /// <param name="ruleCategory">The optional alias for the exact rule category filter.</param>
        /// <param name="status">The optional exact result status filter.</param>
        /// <param name="targetStableKey">The optional exact result target stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application architecture-rule query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the architecture-rule result page, or a validation problem response for invalid query input.</returns>
        private static async Task<IResult> ListSnapshotArchitectureRulesAsync(
            string? snapshotStableKey,
            string? category,
            string? ruleCategory,
            string? status,
            string? targetStableKey,
            int? skip,
            int? take,
            IArchitectureRuleQueryService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys and accepts ruleCategory as a discoverable alias for category.
            try
            {
                string? effectiveCategory = string.IsNullOrWhiteSpace(category) ? ruleCategory : category;
                ArchitectureRuleQuery query = new(snapshotStableKey, effectiveCategory, status, targetStableKey, skip, take);
                PagedQueryResult<ArchitectureRuleItemDto> result = await service.ListArchitectureRulesAsync(query, cancellationToken).ConfigureAwait(false);
                LogPageResult(loggerFactory, "snapshot-architecture-rules", result.TotalCount, result.Skip, result.Take);
                return Results.Ok(ToPagedResponse(result));
            }
            catch (ArgumentException exception)
            {
                return QueryValidationProblemFactory.FromArgumentException(exception, "architectureRules");
            }
        }

        /// <summary>
        /// Handles GET /snapshot-diff by returning a controlled diff result for two snapshot stable keys.
        /// </summary>
        /// <param name="currentSnapshotStableKey">The required current snapshot stable key.</param>
        /// <param name="previousSnapshotStableKey">The required previous snapshot stable key.</param>
        /// <param name="domains">The optional comma-separated or repeated domain filters.</param>
        /// <param name="changeKinds">The optional comma-separated or repeated change-kind filters.</param>
        /// <param name="projectStableKey">The optional owning or related project stable-key filter.</param>
        /// <param name="targetStableKey">The optional target node, edge endpoint, finding target, or metric target stable-key filter.</param>
        /// <param name="recordKind">The optional domain-specific kind filter.</param>
        /// <param name="severity">The optional finding severity filter.</param>
        /// <param name="includeUnchangedDetails">Indicates whether unchanged detail rows should be included.</param>
        /// <param name="skip">The optional number of matching detail rows to skip.</param>
        /// <param name="take">The optional maximum number of matching detail rows to return.</param>
        /// <param name="service">The query service that owns application snapshot diff behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the diff result, or a validation problem response for invalid query input.</returns>
        private static async Task<IResult> GetSnapshotDiffAsync(
            string? currentSnapshotStableKey,
            string? previousSnapshotStableKey,
            string[]? domains,
            string[]? changeKinds,
            string? projectStableKey,
            string? targetStableKey,
            string? recordKind,
            string? severity,
            bool? includeUnchangedDetails,
            int? skip,
            int? take,
            ISnapshotDiffService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Snapshot diff accepts only fixed filters and stable identities; callers never submit arbitrary graph traversal or Cypher.
            SnapshotDiffQuery query = new(currentSnapshotStableKey, previousSnapshotStableKey, domains, changeKinds, includeUnchangedDetails ?? false, projectStableKey, targetStableKey, recordKind, severity, skip, take);
            SnapshotDiffResult result = await service.CompareSnapshotsAsync(query, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {ItemCount} diff items and truncation status {Truncated}.",
                    "snapshot-diff",
                    result.Items.Count,
                    result.Truncation.Truncated);
                return Results.Ok(result);
            }

            return Results.ValidationProblem(result.ValidationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts search query validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the search query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToSearchValidationProblem(IEnumerable<SearchQueryValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, source snippets, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Splits and normalizes search result-kind filters from repeated values or comma-separated API query strings.
        /// </summary>
        /// <param name="filters">The raw result-kind filters supplied by the request.</param>
        /// <returns>A deterministic read-only list of trimmed unique filters.</returns>
        private static IReadOnlyList<string> NormalizeSearchFilters(IEnumerable<string>? filters)
        {
            // Minimal APIs bind comma-containing query strings as a single value, so search accepts both repeated and comma-separated forms.
            return filters is null
                ? []
                : filters
                    .SelectMany(static filter => (filter ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Where(static filter => !string.IsNullOrWhiteSpace(filter))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static filter => filter, StringComparer.Ordinal)
                    .ToArray();
        }

        /// <summary>
        /// Handles GET /snapshot-diff/latest by resolving the latest and previous comparable snapshots before diffing them.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable key that bounds latest resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows latest resolution.</param>
        /// <param name="domains">The optional comma-separated or repeated domain filters.</param>
        /// <param name="changeKinds">The optional comma-separated or repeated change-kind filters.</param>
        /// <param name="projectStableKey">The optional owning or related project stable-key filter.</param>
        /// <param name="targetStableKey">The optional target node, edge endpoint, finding target, or metric target stable-key filter.</param>
        /// <param name="recordKind">The optional domain-specific kind filter.</param>
        /// <param name="severity">The optional finding severity filter.</param>
        /// <param name="includeUnchangedDetails">Indicates whether unchanged detail rows should be included.</param>
        /// <param name="skip">The optional number of matching detail rows to skip.</param>
        /// <param name="take">The optional maximum number of matching detail rows to return.</param>
        /// <param name="service">The query service that owns application snapshot diff behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the diff result, or a validation problem response for invalid query input.</returns>
        private static async Task<IResult> GetLatestSnapshotDiffAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string[]? domains,
            string[]? changeKinds,
            string? projectStableKey,
            string? targetStableKey,
            string? recordKind,
            string? severity,
            bool? includeUnchangedDetails,
            int? skip,
            int? take,
            ISnapshotDiffService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Latest diff uses fixed scope and filters, then delegates current/previous selection to the application layer.
            SnapshotDiffLatestQuery query = new(repositoryStableKey, solutionStableKey, domains, changeKinds, projectStableKey, targetStableKey, recordKind, severity, includeUnchangedDetails ?? false, skip, take);
            SnapshotDiffResult result = await service.CompareLatestToPreviousAsync(query, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {ItemCount} diff items and truncation status {Truncated}.",
                    "snapshot-diff-latest",
                    result.Items.Count,
                    result.Truncation.Truncated);
                return Results.Ok(result);
            }

            return Results.ValidationProblem(result.ValidationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Handles GET /search by returning bounded cross-domain search results for the selected snapshot scope.
        /// </summary>
        /// <param name="repositoryStableKey">The required repository stable-key query parameter.</param>
        /// <param name="solutionStableKey">The optional solution stable-key query parameter.</param>
        /// <param name="snapshotStableKey">The optional exact snapshot stable key or latest selector.</param>
        /// <param name="searchText">The required search text matched against safe public fields.</param>
        /// <param name="resultKinds">The optional comma-separated or repeated result-kind filters.</param>
        /// <param name="projectStableKey">The optional project stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The application query service that owns cross-domain search behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="httpContext">The current HTTP context used for trace and correlation metadata.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the search result page, a validation problem, or a safe server error response.</returns>
        private static async Task<IResult> SearchAcrossDomainsAsync(
            string? repositoryStableKey,
            string? solutionStableKey,
            string? snapshotStableKey,
            string? searchText,
            string[]? resultKinds,
            string? projectStableKey,
            int? skip,
            int? take,
            ISearchQueryService service,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            // The endpoint accepts only controlled result-kind filters and safe snapshot selectors before delegating projection to the application layer.
            try
            {
                SearchSnapshotSelector selector = new(repositoryStableKey, solutionStableKey, snapshotStableKey);
                SearchQuery query = new(selector, searchText, NormalizeSearchFilters(resultKinds), projectStableKey, skip.GetValueOrDefault(0), take.GetValueOrDefault(SearchQueryLimits.DefaultTake));
                SearchResult result = await service.SearchAsync(query, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return ToSearchValidationProblem(result.ValidationErrors);
                }

                QueryPagedApiResponse<SearchResultItemDto> response = ToSearchPagedResponse(result.Page!, result.Context!, httpContext);
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                    "Handled {EndpointName} query with {TotalCount} matching results, skip {Skip}, and take {Take}.",
                    "search",
                    response.TotalCount,
                    response.Skip,
                    response.Take);
                return Results.Ok(response);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is part of normal ASP.NET Core request flow and should remain observable to the host pipeline.
                throw;
            }
            catch (Exception exception)
            {
                // Unexpected failures are logged without stable keys, snippets, or filter values, and the public response omits exception details.
                loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogError(exception, "Cross-domain search query failed with a safe public error response.");
                QueryErrorResponse error = new("SearchFailed", "Cross-domain search could not be completed.", httpContext.TraceIdentifier);
                return Results.Json(error, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Converts fact query validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the fact query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToFactValidationProblem(IEnumerable<FactQueryValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, source snippets, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts symbol query validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the symbol query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToSymbolValidationProblem(IEnumerable<SymbolQueryValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, source snippets, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts runtime query validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the runtime query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToRuntimeValidationProblem(IEnumerable<RuntimeQueryValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, source snippets, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Handles GET /rules by returning a controlled catalog page.
        /// </summary>
        /// <param name="ruleCode">The optional exact rule code filter.</param>
        /// <param name="version">The optional exact rule version filter.</param>
        /// <param name="category">The optional exact category filter.</param>
        /// <param name="severity">The optional exact severity filter.</param>
        /// <param name="enabled">The optional enabled-state filter.</param>
        /// <param name="builtIn">The optional built-in-state filter.</param>
        /// <param name="ownerScope">The optional exact owner-scope filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the catalog page.</returns>
        private static async Task<IResult> ListRulesAsync(
            string? ruleCode,
            string? version,
            string? category,
            string? severity,
            bool? enabled,
            bool? builtIn,
            string? ownerScope,
            int? skip,
            int? take,
            IHotlistQueryService service,
            CancellationToken cancellationToken)
        {
            // Query-string parameters map one-to-one to controlled filters; no raw predicate text is accepted.
            RuleCatalogQuery query = new(ruleCode, version, category, severity, enabled, builtIn, ownerScope, skip, take);
            PagedQueryResult<RuleCatalogItemDto> result = await service.ListRulesAsync(query, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToPagedResponse(result));
        }

        /// <summary>
        /// Handles GET /rules/{ruleCode}/{version} by returning exact rule detail or not found.
        /// </summary>
        /// <param name="ruleCode">The route rule code.</param>
        /// <param name="version">The route rule version.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK rule detail response or a not-found response.</returns>
        private static async Task<IResult> GetRuleAsync(string ruleCode, string version, IHotlistQueryService service, CancellationToken cancellationToken)
        {
            // Blank route values are invalid identities and are treated as not found to avoid leaking validation exceptions.
            if (string.IsNullOrWhiteSpace(ruleCode) || string.IsNullOrWhiteSpace(version))
            {
                return Results.NotFound();
            }

            RuleDetailDto? result = await service.GetRuleAsync(ruleCode, version, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        /// <summary>
        /// Handles GET /hotlist by returning a controlled finding page.
        /// </summary>
        /// <param name="snapshotStableKey">The optional snapshot stable-key filter.</param>
        /// <param name="category">The optional exact category filter.</param>
        /// <param name="severity">The optional exact severity filter.</param>
        /// <param name="status">The optional exact status filter.</param>
        /// <param name="projectStableKey">The optional project stable-key filter.</param>
        /// <param name="affectedNodeStableKey">The optional affected node stable-key filter.</param>
        /// <param name="criticalOnly">Indicates whether the endpoint should return only critical-severity findings.</param>
        /// <param name="legacyDataAccess">Indicates whether the endpoint should return only legacy data-access findings.</param>
        /// <param name="outOfSupport">Indicates whether the endpoint should return only out-of-support findings.</param>
        /// <param name="securitySensitive">Indicates whether the endpoint should return only security-sensitive findings.</param>
        /// <param name="frameworkOnly">Indicates whether the endpoint should return only framework-only findings.</param>
        /// <param name="technology">The optional technology or technology-family filter.</param>
        /// <param name="ruleCode">The optional exact rule-code filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the hotlist page.</returns>
        private static async Task<IResult> ListHotlistAsync(
            string? snapshotStableKey,
            string? category,
            string? severity,
            string? status,
            string? projectStableKey,
            string? affectedNodeStableKey,
            bool? criticalOnly,
            bool? legacyDataAccess,
            bool? outOfSupport,
            bool? securitySensitive,
            bool? frameworkOnly,
            string? technology,
            string? ruleCode,
            int? skip,
            int? take,
            IHotlistQueryService service,
            CancellationToken cancellationToken)
        {
            // The hotlist exposes only fixed filters from the work-item contract and never accepts graph query text.
            HotlistQuery query = new(snapshotStableKey, category, severity, status, projectStableKey, affectedNodeStableKey, criticalOnly, legacyDataAccess, outOfSupport, securitySensitive, frameworkOnly, technology, ruleCode, skip, take);
            PagedQueryResult<HotlistItemDto> result = await service.ListHotlistAsync(query, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToPagedResponse(result));
        }

        /// <summary>
        /// Handles GET /snapshots/{snapshotStableKey}/metrics by returning a controlled metric page.
        /// </summary>
        /// <param name="snapshotStableKey">The route snapshot stable-key filter.</param>
        /// <param name="metricKind">The optional exact metric kind filter.</param>
        /// <param name="scopeKind">The optional exact metric scope kind filter.</param>
        /// <param name="projectStableKey">The optional exact project or architecture node stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application metric query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the metric page, or a validation problem response for invalid query input.</returns>
        private static async Task<IResult> ListSnapshotMetricsAsync(
            string snapshotStableKey,
            string? metricKind,
            string? scopeKind,
            string? projectStableKey,
            int? skip,
            int? take,
            IMetricQueryService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // The metrics endpoint requires a snapshot route identity and accepts only fixed filters, including the node-target filter used by project and graph metrics.
            try
            {
                MetricQuery query = new(snapshotStableKey, metricKind, scopeKind, projectStableKey, skip, take);
                PagedQueryResult<MetricItemDto> result = await service.ListMetricsAsync(query, cancellationToken).ConfigureAwait(false);
                LogPageResult(loggerFactory, "snapshot-metrics", result.TotalCount, result.Skip, result.Take);
                return Results.Ok(ToPagedResponse(result));
            }
            catch (ArgumentException exception)
            {
                return QueryValidationProblemFactory.FromArgumentException(exception, "metrics");
            }
        }

        /// <summary>
        /// Handles GET /snapshot-metrics by returning a controlled metric page from query-string stable keys.
        /// </summary>
        /// <param name="snapshotStableKey">The query-string snapshot stable-key filter.</param>
        /// <param name="metricKind">The optional exact metric kind filter.</param>
        /// <param name="scopeKind">The optional exact metric scope kind filter.</param>
        /// <param name="projectStableKey">The optional exact project or architecture node stable-key filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application metric query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the metric page, or a validation problem response for invalid query input.</returns>
        private static Task<IResult> ListSnapshotMetricsByQueryAsync(
            string? snapshotStableKey,
            string? metricKind,
            string? scopeKind,
            string? projectStableKey,
            int? skip,
            int? take,
            IMetricQueryService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys that include slash separators.
            return ListSnapshotMetricsAsync(snapshotStableKey ?? string.Empty, metricKind, scopeKind, projectStableKey, skip, take, service, loggerFactory, cancellationToken);
        }

        /// <summary>
        /// Handles GET /snapshot-cycles by returning a controlled dependency cycle page from query-string stable keys.
        /// </summary>
        /// <param name="snapshotStableKey">The required query-string snapshot stable-key filter.</param>
        /// <param name="nodeStableKey">The optional exact node stable key that must participate in returned cycles.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        /// <param name="service">The query service that owns application cycle query behavior.</param>
        /// <param name="loggerFactory">The logger factory used to write secret-safe endpoint diagnostics.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK response containing the cycle page, or a validation problem response for invalid query input.</returns>
        private static async Task<IResult> ListSnapshotCyclesAsync(
            string? snapshotStableKey,
            string? nodeStableKey,
            int? skip,
            int? take,
            ICycleQueryService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys that include slash separators.
            try
            {
                CycleQuery query = new(snapshotStableKey, nodeStableKey, skip, take);
                PagedQueryResult<CycleItemDto> result = await service.ListCyclesAsync(query, cancellationToken).ConfigureAwait(false);
                LogPageResult(loggerFactory, "snapshot-cycles", result.TotalCount, result.Skip, result.Take);
                return Results.Ok(ToPagedResponse(result));
            }
            catch (ArgumentException exception)
            {
                return QueryValidationProblemFactory.FromArgumentException(exception, "cycles");
            }
        }

        /// <summary>
        /// Handles GET /findings/{snapshotStableKey}/{findingStableKey} by returning exact finding detail or not found.
        /// </summary>
        /// <param name="snapshotStableKey">The route snapshot stable key.</param>
        /// <param name="findingStableKey">The route finding stable key.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK finding detail response or a not-found response.</returns>
        private static async Task<IResult> GetFindingAsync(string snapshotStableKey, string findingStableKey, IHotlistQueryService service, CancellationToken cancellationToken)
        {
            // Finding stable keys are snapshot-scoped, so both route identities are required.
            if (string.IsNullOrWhiteSpace(snapshotStableKey) || string.IsNullOrWhiteSpace(findingStableKey))
            {
                return Results.NotFound();
            }

            FindingDetailDto? result = await service.GetFindingAsync(snapshotStableKey, findingStableKey, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        /// <summary>
        /// Handles GET /findings/detail by returning exact finding detail from query-string stable keys or not found.
        /// </summary>
        /// <param name="snapshotStableKey">The query-string snapshot stable key.</param>
        /// <param name="findingStableKey">The query-string finding stable key.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK finding detail response or a not-found response.</returns>
        private static async Task<IResult> GetFindingByQueryAsync(string? snapshotStableKey, string? findingStableKey, IHotlistQueryService service, CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for stable keys that include slash separators.
            if (string.IsNullOrWhiteSpace(snapshotStableKey) || string.IsNullOrWhiteSpace(findingStableKey))
            {
                return Results.NotFound();
            }

            FindingDetailDto? result = await service.GetFindingAsync(snapshotStableKey, findingStableKey, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        /// <summary>
        /// Handles GET /findings/history/{historyKey} by returning cross-snapshot finding history or not found.
        /// </summary>
        /// <param name="historyKey">The route finding history key.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK finding history response or a not-found response.</returns>
        private static async Task<IResult> GetFindingHistoryAsync(string historyKey, IHotlistQueryService service, CancellationToken cancellationToken)
        {
            // History keys are deterministic external identities and never map to Neo4j internal IDs.
            if (string.IsNullOrWhiteSpace(historyKey))
            {
                return Results.NotFound();
            }

            FindingHistoryDto? result = await service.GetFindingHistoryAsync(historyKey, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        /// <summary>
        /// Handles GET /finding-history by returning cross-snapshot finding history from a query-string history key.
        /// </summary>
        /// <param name="historyKey">The query-string finding history key.</param>
        /// <param name="service">The query service that owns application query behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK finding history response or a not-found response.</returns>
        private static async Task<IResult> GetFindingHistoryByQueryAsync(string? historyKey, IHotlistQueryService service, CancellationToken cancellationToken)
        {
            // Query-string lookup avoids path decoding ambiguity for history keys that include slash separators.
            if (string.IsNullOrWhiteSpace(historyKey))
            {
                return Results.NotFound();
            }

            FindingHistoryDto? result = await service.GetFindingHistoryAsync(historyKey, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        /// <summary>
        /// Handles POST /findings/suppressions by validating and persisting a suppression overlay.
        /// </summary>
        /// <param name="request">The API request body supplied by the caller.</param>
        /// <param name="service">The query service that owns suppression behavior.</param>
        /// <param name="cancellationToken">The cancellation token associated with the HTTP request.</param>
        /// <returns>An OK suppression response, validation problem response, or controlled server error response.</returns>
        private static async Task<IResult> SuppressFindingAsync(SuppressFindingApiRequest request, IHotlistQueryService service, CancellationToken cancellationToken)
        {
            // Metadata is canonicalized before entering the application layer so invalid metadata fails as validation-like input.
            GraphMetadata metadata;
            try
            {
                metadata = ToMetadata(request.Metadata);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["Metadata"] = [exception.Message]
                });
            }

            SuppressionCommandResult result = await service.SuppressFindingAsync(
                new SuppressFindingCommand(
                    request.FindingHistoryKey,
                    request.RuleCode,
                    request.RuleVersion,
                    request.PrimaryNodeStableKey,
                    request.Reason,
                    request.SuppressedBy,
                    metadata),
                cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                return Results.Ok(new SuppressFindingApiResponse(result.SuppressedCount, result.Warnings));
            }

            if (result.ValidationErrors.Count > 0)
            {
                return Results.ValidationProblem(result.ValidationErrors
                    .GroupBy(error => error.Code, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Message).ToArray(),
                        StringComparer.Ordinal));
            }

            return Results.Problem("Finding suppression could not be persisted.", statusCode: StatusCodes.Status500InternalServerError);
        }

        /// <summary>
        /// Converts an application page into the API response envelope.
        /// </summary>
        /// <typeparam name="TItem">The response item type.</typeparam>
        /// <param name="result">The application paged result.</param>
        /// <returns>The API response envelope.</returns>
        private static PagedApiResponse<TItem> ToPagedResponse<TItem>(PagedQueryResult<TItem> result)
        {
            // Mapping keeps endpoint signatures explicit while preserving application paging metadata.
            return new PagedApiResponse<TItem>(result.Items, result.TotalCount, result.Skip, result.Take);
        }

        /// <summary>
        /// Converts a failed dashboard summary result into a deterministic validation problem response.
        /// </summary>
        /// <param name="result">The failed dashboard summary result.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToDashboardValidationProblem(DashboardSummaryResult result)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, or infrastructure details.
            return Results.ValidationProblem(result.ValidationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts project query validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the project query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToProjectValidationProblem(IEnumerable<ProjectQueryValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts evidence query validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the evidence query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToEvidenceValidationProblem(IEnumerable<EvidenceQueryValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, source snippets, stack traces, stable keys, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts graph traversal validation errors into a deterministic validation problem response.
        /// </summary>
        /// <param name="validationErrors">The validation errors produced by the graph traversal query service.</param>
        /// <returns>A validation problem response with grouped machine-readable error codes.</returns>
        private static IResult ToGraphTraversalValidationProblem(IEnumerable<GraphTraversalValidationError> validationErrors)
        {
            // Validation output groups by stable code and never serializes exceptions, stack traces, stable keys, or infrastructure details.
            return Results.ValidationProblem(validationErrors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Message).ToArray(),
                    StringComparer.Ordinal));
        }

        /// <summary>
        /// Converts an application traversal payload into the WP014 non-paged API response envelope.
        /// </summary>
        /// <param name="data">The application traversal response payload.</param>
        /// <param name="context">The application traversal query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged graph traversal API response.</returns>
        private static QueryApiResponse<GraphTraversalResponseDto> ToGraphTraversalResponse(GraphTraversalResponseDto data, GraphTraversalQueryContext context, HttpContext httpContext)
        {
            // Traversal responses are non-paged but carry explicit truncation metadata because edge result limits can omit reachable graph records.
            return new QueryApiResponse<GraphTraversalResponseDto>(
                data,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                QueryNonPagedMetadataResponse.Summary(),
                ToTruncationResponse(data.Truncation),
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application worker page into the WP014 paged API response envelope.
        /// </summary>
        /// <param name="page">The application worker page.</param>
        /// <param name="context">The application runtime query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged worker API response with worker-specific unknowns promoted to the envelope.</returns>
        private static QueryPagedApiResponse<WorkerDto> ToWorkerPagedResponse(PagedQueryResult<WorkerDto> page, RuntimeQueryContext context, HttpContext httpContext)
        {
            // Worker rows can contain nested queue or schedule unknowns, so those are promoted to envelope metadata for client visibility.
            RuntimeUnknownDto[] unknowns = context.Unknowns
                .Concat(page.Items.SelectMany(static item => item.Unknowns))
                .DistinctBy(static unknown => unknown.Field + "\u001f" + unknown.Reason)
                .ToArray();
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(page.Items.Count);
            return new QueryPagedApiResponse<WorkerDto>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application symbol search page into the WP014 paged API response envelope.
        /// </summary>
        /// <param name="page">The application symbol search page.</param>
        /// <param name="context">The application symbol query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged symbol search API response.</returns>
        private static QueryPagedApiResponse<SymbolSearchItemDto> ToSymbolPagedResponse(PagedQueryResult<SymbolSearchItemDto> page, SymbolQueryContext context, HttpContext httpContext)
        {
            // Symbol search uses the common paged envelope so clients receive scope, snapshot, warnings, unknowns, and request metadata consistently.
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(page.Items.Count);
            return new QueryPagedApiResponse<SymbolSearchItemDto>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application symbol usage page into the WP014 paged API response envelope.
        /// </summary>
        /// <param name="page">The application symbol usage page.</param>
        /// <param name="context">The application symbol query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged symbol usage API response.</returns>
        private static QueryPagedApiResponse<SymbolUsageDto> ToSymbolUsageResponse(PagedQueryResult<SymbolUsageDto> page, SymbolQueryContext context, HttpContext httpContext)
        {
            // Symbol usage is paged because a popular symbol can have many callers or references in a large snapshot.
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(page.Items.Count);
            return new QueryPagedApiResponse<SymbolUsageDto>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application runtime page into the WP014 paged API response envelope.
        /// </summary>
        /// <typeparam name="TItem">The runtime item DTO contained in the page.</typeparam>
        /// <param name="page">The application runtime page.</param>
        /// <param name="context">The application runtime query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged runtime API response.</returns>
        private static QueryPagedApiResponse<TItem> ToRuntimePagedResponse<TItem>(PagedQueryResult<TItem> page, RuntimeQueryContext context, HttpContext httpContext)
        {
            // Runtime list endpoints use the common paged envelope so clients receive scope, snapshot, warnings, unknowns, and request metadata consistently.
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(page.Items.Count);
            return new QueryPagedApiResponse<TItem>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application fact page into the WP014 paged API response envelope.
        /// </summary>
        /// <typeparam name="TItem">The fact item DTO contained in the page.</typeparam>
        /// <param name="page">The application fact page.</param>
        /// <param name="context">The application fact query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged fact API response.</returns>
        private static QueryPagedApiResponse<TItem> ToFactPagedResponse<TItem>(PagedQueryResult<TItem> page, FactQueryContext context, HttpContext httpContext)
        {
            // Fact list endpoints use the common paged envelope so consumers receive scope, snapshot, warnings, unknowns, and request metadata consistently.
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(page.Items.Count);
            return new QueryPagedApiResponse<TItem>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application evidence detail into the WP014 non-paged API response envelope.
        /// </summary>
        /// <param name="detail">The application evidence detail payload.</param>
        /// <param name="context">The application evidence query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged evidence detail API response.</returns>
        private static QueryApiResponse<EvidenceDetailDto> ToEvidenceDetailResponse(EvidenceDetailDto detail, EvidenceQueryContext context, HttpContext httpContext)
        {
            // Evidence detail responses are non-paged but include truncation metadata when the persisted snippet preview was bounded.
            QueryTruncationMetadataResponse truncation = new(detail.SnippetPreview.Truncated, detail.SnippetPreview.Limit, detail.SnippetPreview.ReturnedLength, detail.SnippetPreview.Truncated ? "Snippet preview was bounded to the configured evidence preview limit." : null);
            return new QueryApiResponse<EvidenceDetailDto>(
                detail,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                QueryNonPagedMetadataResponse.Summary(),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application related-evidence page into the WP014 paged API response envelope.
        /// </summary>
        /// <param name="page">The application related-evidence page.</param>
        /// <param name="context">The application evidence query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged related-evidence API response.</returns>
        private static QueryPagedApiResponse<EvidenceDetailDto> ToEvidencePagedResponse(PagedQueryResult<EvidenceDetailDto> page, EvidenceQueryContext context, HttpContext httpContext)
        {
            // Related-evidence responses are paged and additionally report snippet truncation when any returned preview was bounded.
            bool snippetTruncated = page.Items.Any(static item => item.SnippetPreview.Truncated);
            string? reason = snippetTruncated ? "One or more snippet previews were bounded to the configured evidence preview limit." : null;
            QueryTruncationMetadataResponse truncation = new(page.TotalCount > page.Items.Count || snippetTruncated, page.Take, page.Items.Count, reason);
            return new QueryPagedApiResponse<EvidenceDetailDto>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application controller or handler detail into the WP014 non-paged API response envelope.
        /// </summary>
        /// <param name="detail">The application controller or handler detail payload.</param>
        /// <param name="context">The application runtime query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged controller or handler API response.</returns>
        private static QueryApiResponse<ControllerHandlerDetailDto> ToRuntimeDetailResponse(ControllerHandlerDetailDto detail, RuntimeQueryContext context, HttpContext httpContext)
        {
            // Detail responses preserve runtime query-level warnings and unknowns while counting nested evidence and endpoint sections for truncation metadata.
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(detail.Endpoints.Count + detail.Evidence.Count);
            return new QueryApiResponse<ControllerHandlerDetailDto>(
                detail,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                QueryNonPagedMetadataResponse.Summary(),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application dependency-path payload into the WP014 non-paged API response envelope.
        /// </summary>
        /// <param name="data">The application dependency-path response payload.</param>
        /// <param name="context">The application traversal query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged dependency-path API response.</returns>
        private static QueryApiResponse<DependencyPathResponseDto> ToDependencyPathResponse(DependencyPathResponseDto data, GraphTraversalQueryContext context, HttpContext httpContext)
        {
            // Path responses use the same envelope as traversal responses so no-path and unavailable-data states remain machine-readable data.
            return new QueryApiResponse<DependencyPathResponseDto>(
                data,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                QueryNonPagedMetadataResponse.Summary(),
                ToTruncationResponse(data.Truncation),
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts application traversal truncation metadata into the API envelope truncation metadata.
        /// </summary>
        /// <param name="truncation">The application traversal truncation metadata.</param>
        /// <returns>The API truncation metadata response.</returns>
        private static QueryTruncationMetadataResponse ToTruncationResponse(GraphTraversalTruncationDto truncation)
        {
            // Traversal truncation carries explicit limits so clients can decide whether to narrow depth, edge kinds, or result count.
            return new QueryTruncationMetadataResponse(truncation.Truncated, truncation.Limit, truncation.ReturnedCount, truncation.Reason);
        }

        /// <summary>
        /// Converts an application project catalogue page into the WP014 paged API response envelope.
        /// </summary>
        /// <param name="page">The application project catalogue page.</param>
        /// <param name="context">The application project query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged project catalogue API response.</returns>
        private static QueryPagedApiResponse<ProjectCatalogueItemDto> ToProjectPagedResponse(PagedQueryResult<ProjectCatalogueItemDto> page, ProjectQueryContext context, HttpContext httpContext)
        {
            // The envelope keeps project catalogue paging and cross-cutting metadata together for API and future MCP consumers.
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(page.Items.Count);
            return new QueryPagedApiResponse<ProjectCatalogueItemDto>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application search page into the WP014 paged API response envelope.
        /// </summary>
        /// <param name="page">The application search result page.</param>
        /// <param name="context">The application search query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common paged cross-domain search API response.</returns>
        private static QueryPagedApiResponse<SearchResultItemDto> ToSearchPagedResponse(PagedQueryResult<SearchResultItemDto> page, SearchQueryContext context, HttpContext httpContext)
        {
            // Search uses the same paged envelope as project, symbol, runtime, fact, and evidence reads so MCP clients can share metadata handling.
            QueryTruncationMetadataResponse truncation = new(
                page.Skip > 0 || page.Skip + page.Items.Count < page.TotalCount,
                page.TotalCount,
                page.Items.Count,
                page.Skip > 0 || page.Skip + page.Items.Count < page.TotalCount ? "Search results were bounded by skip/take." : null);
            return new QueryPagedApiResponse<SearchResultItemDto>(
                page.Items,
                page.TotalCount,
                page.Skip,
                page.Take,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                truncation,
                context.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                context.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application project detail into the WP014 non-paged API response envelope.
        /// </summary>
        /// <param name="detail">The application project detail payload.</param>
        /// <param name="context">The application project query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged project detail API response.</returns>
        private static QueryApiResponse<ProjectDetailDto> ToProjectDetailResponse(ProjectDetailDto detail, ProjectQueryContext context, HttpContext httpContext)
        {
            // Detail responses merge query-level unknowns with detail-specific unknowns while preserving the same envelope shape as dashboard summary.
            IReadOnlyList<ProjectUnknownDto> unknowns = context.Unknowns.Concat(detail.Unknowns).DistinctBy(static unknown => unknown.Field + "\u001f" + unknown.Reason).ToArray();
            IReadOnlyList<ProjectWarningDto> warnings = context.Warnings.Concat(detail.Warnings).DistinctBy(static warning => warning.Code + "\u001f" + warning.Message).ToArray();
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(
                detail.Evidence.Count
                + detail.EntryPoints.Count
                + detail.References.Count
                + detail.Dependents.Count
                + detail.Packages.Count
                + detail.Endpoints.Count
                + detail.Workers.Count
                + detail.DataAccess.Count
                + detail.ConfigurationKeys.Count
                + detail.Integrations.Count
                + detail.HotlistFindings.Count);
            return new QueryApiResponse<ProjectDetailDto>(
                detail,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                QueryNonPagedMetadataResponse.Summary(),
                truncation,
                warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts an application symbol detail into the WP014 non-paged API response envelope.
        /// </summary>
        /// <param name="detail">The application symbol detail payload.</param>
        /// <param name="context">The application symbol query context.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged symbol detail API response.</returns>
        private static QueryApiResponse<SymbolDetailDto> ToSymbolDetailResponse(SymbolDetailDto detail, SymbolQueryContext context, HttpContext httpContext)
        {
            // Detail responses merge query-level unknowns with detail-specific unknowns so unresolved semantic facts remain explicit.
            IReadOnlyList<SymbolUnknownDto> unknowns = context.Unknowns.Concat(detail.Unknowns).DistinctBy(static unknown => unknown.Field + "\u001f" + unknown.Reason).ToArray();
            IReadOnlyList<SymbolWarningDto> warnings = context.Warnings.Concat(detail.Warnings).DistinctBy(static warning => warning.Code + "\u001f" + warning.Message).ToArray();
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(detail.Evidence.Count + detail.Relationships.Count);
            return new QueryApiResponse<SymbolDetailDto>(
                detail,
                ToScopeResponse(context.Scope),
                ToSnapshotResponse(context.Snapshot),
                QueryNonPagedMetadataResponse.Summary(),
                truncation,
                warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                new QueryRequestMetadataResponse(httpContext.TraceIdentifier, GetCorrelationId(httpContext)));
        }

        /// <summary>
        /// Converts application project scope metadata into the API envelope scope metadata.
        /// </summary>
        /// <param name="scope">The application project scope metadata.</param>
        /// <returns>The API scope metadata response.</returns>
        private static QueryScopeMetadataResponse ToScopeResponse(ProjectScopeDto scope)
        {
            // Scope conversion preserves stable repository and solution identities without adding persistence-specific data.
            return new QueryScopeMetadataResponse(scope.RepositoryStableKey, scope.RepositoryName, scope.SolutionStableKey, scope.SolutionName);
        }

        /// <summary>
        /// Converts application project snapshot metadata into the API envelope snapshot metadata.
        /// </summary>
        /// <param name="snapshot">The application project snapshot metadata.</param>
        /// <returns>The API snapshot metadata response.</returns>
        private static QuerySnapshotMetadataResponse ToSnapshotResponse(ProjectSnapshotMetadataDto snapshot)
        {
            // Snapshot conversion makes exact-versus-latest resolution visible for every project query response.
            return new QuerySnapshotMetadataResponse(snapshot.SnapshotStableKey, snapshot.Selector, snapshot.ResolvedAsLatest, snapshot.CommitSha, snapshot.StartedUtc, snapshot.CompletedUtc, snapshot.Status);
        }

        /// <summary>
        /// Converts the application dashboard summary into the common WP014 API response envelope.
        /// </summary>
        /// <param name="summary">The application dashboard summary.</param>
        /// <param name="httpContext">The HTTP context that supplies trace and correlation metadata.</param>
        /// <returns>The common non-paged dashboard summary API response.</returns>
        private static QueryApiResponse<DashboardSummaryDto> ToDashboardResponse(DashboardSummaryDto summary, HttpContext httpContext)
        {
            // The envelope keeps cross-cutting metadata consistent while the data payload remains the application-owned dashboard contract.
            QueryScopeMetadataResponse scope = new(
                summary.Scope.RepositoryStableKey,
                summary.Scope.RepositoryName,
                summary.Scope.SolutionStableKey,
                summary.Scope.SolutionName);
            QuerySnapshotMetadataResponse snapshot = new(
                summary.Snapshot.SnapshotStableKey,
                summary.Snapshot.Selector,
                summary.Snapshot.ResolvedAsLatest,
                summary.Snapshot.CommitSha,
                summary.Snapshot.StartedUtc,
                summary.Snapshot.CompletedUtc,
                summary.Snapshot.Status);
            QueryTruncationMetadataResponse truncation = QueryTruncationMetadataResponse.None(summary.TopHotspots.Count + summary.LatestChanges.Count);
            QueryRequestMetadataResponse request = new(httpContext.TraceIdentifier, GetCorrelationId(httpContext));
            return new QueryApiResponse<DashboardSummaryDto>(
                summary,
                scope,
                snapshot,
                QueryNonPagedMetadataResponse.Summary(),
                truncation,
                summary.Warnings.Select(static warning => new QueryWarningResponse(warning.Code, warning.Message)),
                summary.Unknowns.Select(static unknown => new QueryUnknownResponse(unknown.Field, unknown.Reason)),
                request);
        }

        /// <summary>
        /// Reads an optional caller-supplied correlation identifier from request headers.
        /// </summary>
        /// <param name="httpContext">The HTTP context containing request headers.</param>
        /// <returns>The safe correlation identifier when one was supplied; otherwise, null.</returns>
        private static string? GetCorrelationId(HttpContext httpContext)
        {
            // Correlation identifiers are bounded to a single header value and trimmed so the envelope does not echo arbitrary header collections.
            string? correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out Microsoft.Extensions.Primitives.StringValues values)
                ? values.FirstOrDefault()
                : null;
            return string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        }

        /// <summary>
        /// Writes a secret-safe operational diagnostic for one controlled paged query.
        /// </summary>
        /// <param name="loggerFactory">The logger factory resolved from endpoint dependency injection.</param>
        /// <param name="endpointName">The fixed endpoint name being logged.</param>
        /// <param name="totalCount">The total number of records matched before paging.</param>
        /// <param name="skip">The validated skip value used by the query.</param>
        /// <param name="take">The validated take value used by the query.</param>
        private static void LogPageResult(ILoggerFactory loggerFactory, string endpointName, int totalCount, int skip, int take)
        {
            // Stable keys and metadata are intentionally omitted so diagnostics cannot leak secrets or repository-specific identities.
            loggerFactory.CreateLogger(typeof(QueryEndpointRouteBuilderExtensions)).LogInformation(
                "Handled {EndpointName} query with {TotalCount} total records, skip {Skip}, and take {Take}.",
                endpointName,
                totalCount,
                skip,
                take);
        }

        /// <summary>
        /// Converts request metadata JSON elements into deterministic graph metadata.
        /// </summary>
        /// <param name="metadata">The optional request metadata dictionary.</param>
        /// <returns>Canonical graph metadata for the application command.</returns>
        private static GraphMetadata ToMetadata(IReadOnlyDictionary<string, JsonElement>? metadata)
        {
            // The API accepts only object-style metadata fields and lets the domain metadata factory enforce key rules.
            if (metadata is null || metadata.Count == 0)
            {
                return GraphMetadata.Empty;
            }

            Dictionary<string, object?> values = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> pair in metadata)
            {
                values[pair.Key] = pair.Value.Clone();
            }

            return GraphMetadata.From(values);
        }
    }
}
