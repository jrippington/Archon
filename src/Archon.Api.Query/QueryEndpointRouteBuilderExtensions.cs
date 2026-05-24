using System.Text.Json;
using Archon.Api.Query.Contracts;
using Archon.Application.ArchitectureRules;
using Archon.Application.Cycles;
using Archon.Application.Diff;
using Archon.Application.Hotspots;
using Archon.Application.Metrics;
using Archon.Application.Rules;
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
                .WithDescription("Returns a bounded, deterministically ordered page of persisted findings using controlled snapshot, category, severity, status, project, and affected-node filters.")
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
            bool? includeUnchangedDetails,
            int? skip,
            int? take,
            ISnapshotDiffService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
        {
            // Snapshot diff accepts only fixed filters and stable identities; callers never submit arbitrary graph traversal or Cypher.
            SnapshotDiffQuery query = new(currentSnapshotStableKey, previousSnapshotStableKey, domains, changeKinds, includeUnchangedDetails ?? false, skip, take);
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
            int? skip,
            int? take,
            IHotlistQueryService service,
            CancellationToken cancellationToken)
        {
            // The hotlist exposes only fixed filters from the work-item contract and never accepts graph query text.
            HotlistQuery query = new(snapshotStableKey, category, severity, status, projectStableKey, affectedNodeStableKey, skip, take);
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
