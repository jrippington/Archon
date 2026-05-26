using Archon.Application.Search;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Implements the read-only <c>archon.search</c> MCP tool over the approved application/query search abstraction.
    /// </summary>
    public sealed class ArchonMcpSearchTool : IArchonMcpSearchTool
    {
        /// <summary>
        /// Executes cross-cutting authorization, allow-listing, and audit behavior before search logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes controlled cross-domain search over persisted architecture snapshots.
        /// </summary>
        private readonly ISearchQueryService _searchQueryService;

        /// <summary>
        /// Applies response-size limits and produces truncation metadata for MCP envelopes.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates an MCP search tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="searchQueryService">The query-layer search abstraction used instead of direct persistence access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        public ArchonMcpSearchTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            ISearchQueryService searchQueryService,
            ArchonMcpLimitGuard limitGuard)
        {
            // Dependencies are injected so tests can replace the query service and so the handler never reaches Neo4j or files directly.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _searchQueryService = searchQueryService ?? throw new ArgumentNullException(nameof(searchQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
        }

        /// <inheritdoc />
        public async Task<object> SearchAsync(ArchonMcpSearchRequest request, CancellationToken cancellationToken)
        {
            // The public handler delegates to the operation executor first so authorization runs before validation or query work.
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyDictionary<string, string> auditParameters = CreateAuditParameters(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpSearchOperation.Name,
                auditParameters,
                () => ExecuteAuthorizedSearchAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, query mapping, and response-envelope mapping after security checks have allowed the operation.
        /// </summary>
        /// <param name="request">The caller-supplied MCP search request.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer execution.</param>
        /// <returns>A boxed MCP success or error payload for the operation executor.</returns>
        private async Task<object> ExecuteAuthorizedSearchAsync(ArchonMcpSearchRequest request, CancellationToken cancellationToken)
        {
            // Validation happens inside the authorized delegate so disabled or unauthorized calls cannot infer request-shape details.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            SearchQuery query = CreateSearchQuery(request);
            SearchResult searchResult;
            try
            {
                // Query-layer failures are converted into the shared safe error vocabulary rather than leaking exception details.
                searchResult = await _searchQueryService.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains visible to the host executor and should not be converted into a query error.
                throw;
            }
            catch (Exception)
            {
                // The response deliberately omits exception type, message, and stack trace because query dependencies can carry internals.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpSearchOperation.Name,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The search query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry the search or narrow the scope after verifying query data is available.", "user.question", null)]);
            }

            if (!searchResult.Succeeded)
            {
                return MapSearchFailure(searchResult);
            }

            return MapSearchSuccess(request, searchResult);
        }

        /// <summary>
        /// Validates the common fields and search-specific scope fields for one MCP search request.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpSearchRequest request)
        {
            // Shared validation covers search text, snapshot selectors, result-type tokens, project stable keys, and caller limits.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                request.ProjectStableKey,
                request.SnapshotSelector,
                request.SearchText,
                request.ResultTypeFilters,
                request.Limit,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Creates the controlled application-layer search query from the MCP request.
        /// </summary>
        /// <param name="request">The validated MCP search request.</param>
        /// <returns>A controlled query-layer search request.</returns>
        private static SearchQuery CreateSearchQuery(ArchonMcpSearchRequest request)
        {
            // The query-layer selector remains the only representation that resolves latest/current snapshot scope.
            SearchSnapshotSelector selector = new(
                request.RepositoryStableKey,
                request.SolutionStableKey,
                request.SnapshotSelector);
            string[] resultKinds = request.ResultTypeFilters?
                .Where(filter => !string.IsNullOrWhiteSpace(filter))
                .Select(filter => filter.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(filter => filter, StringComparer.Ordinal)
                .ToArray() ?? [];
            int take = request.Limit.GetValueOrDefault(SearchQueryLimits.DefaultTake);

            return new SearchQuery(
                selector,
                request.SearchText?.Trim(),
                resultKinds,
                string.IsNullOrWhiteSpace(request.ProjectStableKey) ? null : request.ProjectStableKey.Trim(),
                Skip: 0,
                take);
        }

        /// <summary>
        /// Maps a successful query-layer search result into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP search request.</param>
        /// <param name="searchResult">The successful query-layer search result.</param>
        /// <returns>A typed MCP success envelope containing grouped search facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpSearchFacts> MapSearchSuccess(ArchonMcpSearchRequest request, SearchResult searchResult)
        {
            // Successful query results always include context and a page; validation failure paths returned before this method.
            SearchQueryContext context = searchResult.Context ?? throw new InvalidOperationException("Search context was not returned for a successful search result.");
            IReadOnlyList<SearchResultItemDto> queryItems = searchResult.Page?.Items ?? [];
            ArchonMcpLimitedList<SearchResultItemDto> limitedItems = _limitGuard.ApplyResultLimit(queryItems, request.Limit, ArchonMcpSearchOperation.Name);
            ArchonMcpSearchResultItem[] mappedItems = limitedItems.Items
                .Select(MapResultItem)
                .OrderBy(item => GetResultKindOrder(item.EntityKind))
                .ThenBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .ToArray();
            ArchonMcpSearchResultGroup[] groups = mappedItems
                .GroupBy(item => item.EntityKind, StringComparer.Ordinal)
                .OrderBy(group => GetResultKindOrder(group.Key))
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ArchonMcpSearchResultGroup(group.Key, group.ToArray()))
                .ToArray();
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateEvidenceReferences(mappedItems, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUnknowns(mappedItems, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateWarnings(context, limitedItems.Limits);
            IReadOnlyList<ArchonMcpSuggestedFollowUp> followUps = CreateResponseFollowUps(mappedItems, limitedItems.SuggestedFollowUps);
            ArchonMcpSearchFacts facts = new(
                request.SearchText?.Trim() ?? string.Empty,
                context.Scope.RepositoryStableKey,
                context.Scope.SolutionStableKey,
                request.ProjectStableKey,
                searchResult.Page?.TotalCount ?? mappedItems.Length,
                mappedItems.Length,
                dataAvailable: true,
                groups);
            ArchonMcpConfidence confidence = CreateOverallConfidence(mappedItems, unknowns, DataAvailable: true);
            string summary = CreateSummary(mappedItems.Length, facts.TotalMatches, request.SearchText, DataAvailable: true);

            return new ArchonMcpEnvelope<ArchonMcpSearchFacts>(
                ArchonMcpSearchOperation.Name,
                CreateSnapshotIdentity(context),
                summary,
                confidence,
                facts,
                evidence,
                findings: null,
                unknowns,
                warnings,
                limitedItems.Limits,
                followUps);
        }

        /// <summary>
        /// Maps query-layer validation or availability failures into safe MCP error envelopes.
        /// </summary>
        /// <param name="searchResult">The failed query-layer search result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapSearchFailure(SearchResult searchResult)
        {
            // Repository or snapshot lookup failures mean the search data is unavailable for the supplied scope, not merely empty.
            bool unavailable = searchResult.ValidationErrors.Any(error =>
                string.Equals(error.Code, SearchQueryValidationCodes.RepositoryNotFound, StringComparison.Ordinal) ||
                string.Equals(error.Code, SearchQueryValidationCodes.SolutionNotFound, StringComparison.Ordinal) ||
                string.Equals(error.Code, SearchQueryValidationCodes.SnapshotNotFound, StringComparison.Ordinal));
            string message = unavailable
                ? "Search data is unavailable for the requested repository, solution, or snapshot scope."
                : string.Join(" ", searchResult.ValidationErrors.Select(error => error.Message));
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : ArchonMcpErrorCategory.Validation;

            return ArchonMcpErrorResponse.Create(
                ArchonMcpSearchOperation.Name,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check the repository and snapshot stable keys, then retry archon.search.", "user.question", null)]);
        }

        /// <summary>
        /// Creates a validation error envelope from MCP request validation failures.
        /// </summary>
        /// <param name="validationResult">The validation result produced before query execution.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // The message is deliberately concise and field-focused so it does not echo raw request payloads or unsafe text.
            string message = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                ArchonMcpSearchOperation.Name,
                ArchonMcpErrorCategory.Validation,
                message,
                [new ArchonMcpSuggestedFollowUp("Correct the search text, scope filters, result-type filters, and limit before retrying.", "user.question", null)]);
        }

        /// <summary>
        /// Maps one query-layer search item into the MCP search fact contract.
        /// </summary>
        /// <param name="item">The query-layer result item to map.</param>
        /// <returns>A safe MCP search result item.</returns>
        private static ArchonMcpSearchResultItem MapResultItem(SearchResultItemDto item)
        {
            // Result-level follow-ups remain constrained to Archon routes and stable-key parameters supplied by the query layer.
            ArchonMcpSearchSuggestedFollowUp[] followUps = item.FollowUps
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Route, StringComparer.Ordinal)
                .Select(followUp => new ArchonMcpSearchSuggestedFollowUp(followUp.Label, followUp.Route, followUp.Parameters))
                .ToArray();

            return new ArchonMcpSearchResultItem(
                item.StableKey,
                item.ResultKind,
                item.DisplayText,
                item.Summary,
                item.SnapshotStableKey,
                item.EvidenceStableKeys,
                item.RelatedNodeStableKeys,
                item.HasUnknownData,
                item.UnknownReason,
                MapConfidence(item.Confidence).Level.ToString(),
                followUps);
        }

        /// <summary>
        /// Creates stable evidence references from result-level evidence stable keys.
        /// </summary>
        /// <param name="items">The mapped MCP search items.</param>
        /// <param name="context">The query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private static IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IEnumerable<ArchonMcpSearchResultItem> items, SearchQueryContext context)
        {
            // Search rows currently expose evidence stable keys rather than rich evidence records, so references avoid invented source spans.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return items
                .SelectMany(item => item.EvidenceStableKeys.Select(evidenceStableKey => new { evidenceStableKey, item }))
                .GroupBy(pair => pair.evidenceStableKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ArchonMcpEvidenceReference(
                    group.Key,
                    "SearchEvidenceReference",
                    sourcePath: null,
                    startLine: null,
                    endLine: null,
                    symbolName: null,
                    containingSymbol: null,
                    snippetPreview: null,
                    snippetHash: null,
                    MapConfidenceForEvidence(group.First().item.Confidence),
                    snapshot))
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknown records from result rows and query context unknowns.
        /// </summary>
        /// <param name="items">The mapped MCP search items.</param>
        /// <param name="context">The query context containing query-wide unknowns.</param>
        /// <returns>Deterministically ordered unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(IEnumerable<ArchonMcpSearchResultItem> items, SearchQueryContext context)
        {
            // Unknowns distinguish unsupported or incomplete search data from a known absence of results.
            List<ArchonMcpUnknown> unknowns = [];
            unknowns.AddRange(context.Unknowns
                .OrderBy(unknown => unknown.Field, StringComparer.Ordinal)
                .Select(unknown => new ArchonMcpUnknown(
                    unknown.Field,
                    affectedStableKey: null,
                    unknown.Reason,
                    "Search confidence is reduced because the query layer reported incomplete or unavailable data.",
                    new ArchonMcpSuggestedFollowUp("Narrow the search scope or inspect related evidence.", "user.question", null))));
            unknowns.AddRange(items
                .Where(item => item.HasUnknownData && !string.IsNullOrWhiteSpace(item.UnknownReason))
                .OrderBy(item => item.StableKey, StringComparer.Ordinal)
                .Select(item => new ArchonMcpUnknown(
                    "searchResultUnknownData",
                    item.StableKey,
                    item.UnknownReason!,
                    "The specific result may be incomplete because persisted facts include unknown state.",
                    new ArchonMcpSuggestedFollowUp("Inspect the matched record with a stable-key-specific tool when available.", "user.question", new Dictionary<string, string> { ["stableKey"] = item.StableKey }))));

            return unknowns;
        }

        /// <summary>
        /// Creates safe warnings from query warnings and MCP limit metadata.
        /// </summary>
        /// <param name="context">The query context containing warning DTOs.</param>
        /// <param name="limits">The limit metadata produced by MCP result limiting.</param>
        /// <returns>Deterministically ordered safe warnings.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(SearchQueryContext context, ArchonMcpLimitMetadata limits)
        {
            // Limit truncation is elevated to a warning so AI clients do not overstate completeness.
            List<ArchonMcpWarning> warnings = context.Warnings
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .ToList();
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("mcp.search.truncated", limits.Reason ?? "Search results were truncated by MCP limits.", affectedStableKey: null));
            }

            return warnings;
        }

        /// <summary>
        /// Creates response-wide suggested follow-ups from result-level affordances and limit suggestions.
        /// </summary>
        /// <param name="items">The mapped MCP search items.</param>
        /// <param name="limitFollowUps">The follow-ups generated by MCP limit enforcement.</param>
        /// <returns>Deterministically ordered response-wide follow-ups.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateResponseFollowUps(IEnumerable<ArchonMcpSearchResultItem> items, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Response-wide follow-ups include a small stable set of result affordances plus any narrowing advice caused by truncation.
            List<ArchonMcpSuggestedFollowUp> followUps = [];
            followUps.AddRange(limitFollowUps);
            followUps.AddRange(items
                .SelectMany(item => item.SuggestedFollowUps.Select(followUp => new ArchonMcpSuggestedFollowUp(followUp.Label, followUp.Operation, followUp.Parameters)))
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Operation, StringComparer.Ordinal)
                .Take(5));

            if (followUps.Count == 0)
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Try a narrower architecture search term or add a result-type filter.", "user.question", null));
            }

            return followUps;
        }

        /// <summary>
        /// Creates a safe audit-parameter dictionary for the operation executor.
        /// </summary>
        /// <param name="request">The request whose non-sensitive fields should be captured for audit.</param>
        /// <returns>Safe request parameters for audit normalization.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(ArchonMcpSearchRequest request)
        {
            // Audit parameters include scope and filter shape but avoid raw result payloads or evidence snippets.
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(parameters, "searchText", request.SearchText);
            AddIfPresent(parameters, "snapshotSelector", request.SnapshotSelector);
            AddIfPresent(parameters, "repositoryStableKey", request.RepositoryStableKey);
            AddIfPresent(parameters, "solutionStableKey", request.SolutionStableKey);
            AddIfPresent(parameters, "projectStableKey", request.ProjectStableKey);
            if (request.ResultTypeFilters is { Count: > 0 })
            {
                parameters["resultTypeFilters"] = string.Join(",", request.ResultTypeFilters);
            }

            if (request.Limit is not null)
            {
                parameters["limit"] = request.Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return parameters;
        }

        /// <summary>
        /// Adds a trimmed parameter value to a dictionary when the value is meaningful.
        /// </summary>
        /// <param name="parameters">The dictionary that receives the value.</param>
        /// <param name="name">The safe parameter name.</param>
        /// <param name="value">The optional value to add.</param>
        private static void AddIfPresent(IDictionary<string, string> parameters, string name, string? value)
        {
            // Blank values are omitted so audit output reflects caller intent without noisy whitespace fields.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }

        /// <summary>
        /// Creates snapshot identity metadata for the MCP envelope.
        /// </summary>
        /// <param name="context">The query context containing resolved snapshot metadata.</param>
        /// <returns>A snapshot identity suitable for the common MCP envelope.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(SearchQueryContext context)
        {
            // Snapshot identity is explicit because search results are meaningful only for one persisted architecture state.
            string mode = context.Snapshot.ResolvedAsLatest ? "latest" : "explicit";
            return new ArchonMcpSnapshotIdentity(
                context.Snapshot.SnapshotStableKey,
                mode,
                $"Resolved from selector '{context.Snapshot.Selector}'.");
        }

        /// <summary>
        /// Creates the response-level confidence value from result and unknown data.
        /// </summary>
        /// <param name="items">The mapped MCP search results.</param>
        /// <param name="unknowns">The explicit unknowns returned with the response.</param>
        /// <param name="DataAvailable">Indicates whether search data was available for the request.</param>
        /// <returns>The overall MCP confidence for the response.</returns>
        private static ArchonMcpConfidence CreateOverallConfidence(IReadOnlyList<ArchonMcpSearchResultItem> items, IReadOnlyList<ArchonMcpUnknown> unknowns, bool DataAvailable)
        {
            // Overall confidence is lowered for unavailable data or explicit unknowns rather than overstating completeness.
            if (!DataAvailable)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "Search data was unavailable for the requested scope.");
            }

            if (items.Count == 0 || unknowns.Count > 0)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Search completed but returned no matches or included explicit unknowns.");
            }

            return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Search results were returned from the controlled application query layer.");
        }

        /// <summary>
        /// Creates a concise response summary for AI client context.
        /// </summary>
        /// <param name="returnedCount">The number of returned MCP results.</param>
        /// <param name="totalCount">The total number of matches reported by the query layer.</param>
        /// <param name="searchText">The original search text supplied by the caller.</param>
        /// <param name="DataAvailable">Indicates whether search data was available for the request.</param>
        /// <returns>A safe summary string.</returns>
        private static string CreateSummary(int returnedCount, int totalCount, string? searchText, bool DataAvailable)
        {
            // The summary avoids inventing interpretation and reports only counts and the normalized search term.
            if (!DataAvailable)
            {
                return "Search data was unavailable for the requested scope.";
            }

            string normalizedText = string.IsNullOrWhiteSpace(searchText) ? "the supplied text" : $"'{searchText.Trim()}'";
            return returnedCount == 0
                ? $"No persisted architecture records matched {normalizedText}."
                : $"Returned {returnedCount} of {totalCount} persisted architecture search matches for {normalizedText}.";
        }

        /// <summary>
        /// Maps a query-layer numeric confidence into the MCP confidence vocabulary.
        /// </summary>
        /// <param name="confidence">The query-layer confidence value.</param>
        /// <returns>An MCP confidence value with a safe reason.</returns>
        private static ArchonMcpConfidence MapConfidence(decimal confidence)
        {
            // The application layer uses a normalized decimal; MCP uses a compact qualitative classification.
            if (confidence >= 0.75m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "The query layer reported high confidence for the match.");
            }

            if (confidence >= 0.4m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "The query layer reported medium confidence for the match.");
            }

            if (confidence > 0m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Low, "The query layer reported low confidence for the match.");
            }

            return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "The query layer did not provide meaningful confidence for the match.");
        }

        /// <summary>
        /// Maps a string confidence value from a result item back to evidence-reference confidence.
        /// </summary>
        /// <param name="confidence">The result item's qualitative confidence value.</param>
        /// <returns>An MCP confidence value for an evidence reference.</returns>
        private static ArchonMcpConfidence MapConfidenceForEvidence(string confidence)
        {
            // Evidence references created from search rows inherit the row-level confidence classification.
            return Enum.TryParse(confidence, out ArchonMcpConfidenceLevel level)
                ? new ArchonMcpConfidence(level, "Evidence reference was associated with a search result at this confidence level.")
                : new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "Evidence reference confidence could not be mapped from the search row.");
        }

        /// <summary>
        /// Gets the deterministic sort order for supported result kinds.
        /// </summary>
        /// <param name="resultKind">The result kind to order.</param>
        /// <returns>A stable ordering value.</returns>
        private static int GetResultKindOrder(string resultKind)
        {
            // Ordering mirrors the application-layer search kind vocabulary and keeps MCP group output stable.
            return resultKind switch
            {
                SearchResultKinds.Project => 0,
                SearchResultKinds.Symbol => 1,
                SearchResultKinds.RuntimeEndpoint => 2,
                SearchResultKinds.Fact => 3,
                SearchResultKinds.Evidence => 4,
                SearchResultKinds.Finding => 5,
                SearchResultKinds.Metric => 6,
                _ => 100
            };
        }
    }
}
