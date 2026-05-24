using System.Text.Json;
using Archon.Api.Query.Contracts;
using Archon.Application.Rules;
using Archon.Domain.Graph.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
