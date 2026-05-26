using Archon.Application.Rules;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpHotlist
{
    /// <summary>
    /// Implements the read-only MCP hotlist findings tool over the approved hotlist query abstraction.
    /// </summary>
    public sealed class ArchonMcpHotlistTool : IArchonMcpHotlistTool
    {
        /// <summary>
        /// Stores supported deterministic sort fields for hotlist findings.
        /// </summary>
        private static readonly string[] s_supportedSortFields = ["severity", "latestSeen", "ruleCode", "stableKey"];

        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before hotlist query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes bounded hotlist finding queries through the application layer.
        /// </summary>
        private readonly IHotlistQueryService _hotlistQueryService;

        /// <summary>
        /// Applies configured MCP response limits to finding records.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates safe evidence references for finding output.
        /// </summary>
        private readonly IArchonMcpResponseMapper _responseMapper;

        /// <summary>
        /// Creates a hotlist MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="hotlistQueryService">The query-layer hotlist abstraction used instead of direct graph or filesystem access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        /// <param name="responseMapper">The mapper that creates secret-safe evidence references.</param>
        public ArchonMcpHotlistTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IHotlistQueryService hotlistQueryService,
            ArchonMcpLimitGuard limitGuard,
            IArchonMcpResponseMapper responseMapper)
        {
            // Constructor injection keeps hotlist behavior testable and aligned with existing MCP query seams.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _hotlistQueryService = hotlistQueryService ?? throw new ArgumentNullException(nameof(hotlistQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
            _responseMapper = responseMapper ?? throw new ArgumentNullException(nameof(responseMapper));
        }

        /// <inheritdoc />
        public async Task<object> GetHotlistFindingsAsync(ArchonMcpHotlistFindingsRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized hotlist requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpHotlistOperations.GetHotlistFindings,
                CreateAuditParameters(request),
                () => ExecuteAuthorizedGetHotlistFindingsAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, hotlist query, and envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized hotlist findings request.</param>
        /// <param name="cancellationToken">The token that can cancel hotlist query execution.</param>
        /// <returns>A hotlist findings envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedGetHotlistFindingsAsync(ArchonMcpHotlistFindingsRequest request, CancellationToken cancellationToken)
        {
            // Validation remains inside the authorized delegate to preserve shared MCP fail-closed ordering.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            PagedQueryResult<HotlistItemDto> page;
            try
            {
                // The application query service owns finding scope/filter behavior and prevents arbitrary graph predicates.
                page = await _hotlistQueryService.ListHotlistAsync(CreateQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be serialized as a query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because finding dependencies can contain persistence internals.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpHotlistOperations.GetHotlistFindings,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The hotlist findings query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry hotlist lookup after verifying finding query data is available.", "user.question", null)]);
            }

            return MapSuccess(request, page);
        }

        /// <summary>
        /// Validates hotlist filters, snapshot scope, search text, sort field, and result limits.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpHotlistFindingsRequest request)
        {
            // Common validation handles stable keys, snapshot selector, text length, filter emptiness, and result-count bounds.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                StableKey: null,
                request.SnapshotSelector,
                request.SearchText,
                CreateFilterList(request.RuleCode, request.Category, request.Severity, request.Status, request.SortBy),
                request.Limit,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.ProjectStableKey, nameof(request.ProjectStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);
            AddTextFilterFailure(failures, request.RuleCode, nameof(request.RuleCode));
            AddTextFilterFailure(failures, request.Category, nameof(request.Category));
            AddTextFilterFailure(failures, request.Severity, nameof(request.Severity));
            AddTextFilterFailure(failures, request.Status, nameof(request.Status));
            if (request.Limit is < 1)
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.Limit), "Limit must be one or greater when supplied."));
            }

            if (request.SortBy is not null && !s_supportedSortFields.Contains(request.SortBy, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.SortBy), "SortBy must be severity, latestSeen, ruleCode, or stableKey when supplied."));
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Creates a controlled application-layer hotlist query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP hotlist request.</param>
        /// <returns>A hotlist query for the application layer.</returns>
        private static HotlistQuery CreateQuery(ArchonMcpHotlistFindingsRequest request)
        {
            // Text search is applied over safe returned summaries because the current query seam exposes fixed structured filters only.
            int take = request.Limit.GetValueOrDefault(HotlistQuery.DefaultPageSize);
            return new HotlistQuery(
                request.SnapshotSelector,
                request.Category,
                request.Severity,
                request.Status,
                request.ProjectStableKey,
                affectedNodeStableKey: null,
                criticalOnly: null,
                legacyDataAccess: null,
                outOfSupport: null,
                securitySensitive: null,
                frameworkOnly: null,
                technology: null,
                request.RuleCode,
                skip: 0,
                take);
        }

        /// <summary>
        /// Maps a successful hotlist page into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP request containing caller filters and limits.</param>
        /// <param name="page">The successful query-layer hotlist page.</param>
        /// <returns>A typed MCP success envelope containing hotlist finding facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpHotlistFindingsFacts> MapSuccess(ArchonMcpHotlistFindingsRequest request, PagedQueryResult<HotlistItemDto> page)
        {
            // MCP applies safe text search, deterministic sorting, and response limiting after the fixed query-layer filters run.
            ArchonMcpHotlistFindingRecord[] records = page.Items
                .Where(item => MatchesSearch(item, request.SearchText))
                .Select(MapRecord)
                .ToArray();
            records = SortRecords(records, request.SortBy);
            ArchonMcpLimitedList<ArchonMcpHotlistFindingRecord> limitedRecords = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpHotlistOperations.GetHotlistFindings);
            ArchonMcpHotlistFindingsFacts facts = new(
                request.SnapshotSelector,
                request.ProjectStableKey,
                request.RuleCode,
                request.Category,
                request.Severity,
                request.Status,
                request.SearchText,
                NormalizeSortBy(request.SortBy),
                records.Length,
                limitedRecords.Items);

            return new ArchonMcpEnvelope<ArchonMcpHotlistFindingsFacts>(
                ArchonMcpHotlistOperations.GetHotlistFindings,
                CreateSnapshotIdentity(request, limitedRecords.Items),
                CreateSummary(facts),
                CreateConfidence(limitedRecords.Items),
                facts,
                CreateEvidenceReferences(limitedRecords.Items),
                CreateFindingReferences(limitedRecords.Items),
                CreateUnknowns(limitedRecords.Items),
                CreateWarnings(limitedRecords.Limits),
                limitedRecords.Limits,
                CreateFollowUps(facts, limitedRecords.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps one query-layer hotlist item into the MCP record shape.
        /// </summary>
        /// <param name="item">The query-layer hotlist item.</param>
        /// <returns>The MCP hotlist finding record.</returns>
        private static ArchonMcpHotlistFindingRecord MapRecord(HotlistItemDto item)
        {
            // Current hotlist list items do not expose first/latest seen timestamps, so metadata states that history lookup is required.
            ArchonMcpAffectedNodeFacts[] affectedNodes = item.AffectedNodes
                .Select(node => new ArchonMcpAffectedNodeFacts(node.StableKey, node.DisplayName, node.NodeKind, node.ProjectStableKey))
                .OrderBy(node => node.StableKey, StringComparer.Ordinal)
                .ToArray();
            string[] evidenceKeys = item.EvidenceReferences.Select(evidence => evidence.StableKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            Dictionary<string, string> metadata = new(StringComparer.Ordinal)
            {
                ["historyKey"] = item.HistoryKey,
                ["historyTimestamps"] = "Use finding history to retrieve firstSeen and latestSeen when required."
            };

            return new ArchonMcpHotlistFindingRecord(
                item.SnapshotStableKey,
                item.StableKey,
                item.HistoryKey,
                item.RuleCode,
                item.RuleVersion,
                item.Title,
                item.Summary,
                item.Severity,
                item.Status,
                item.Confidence,
                item.Category,
                FirstSeen: null,
                LatestSeen: null,
                affectedNodes,
                evidenceKeys,
                metadata);
        }

        /// <summary>
        /// Determines whether a hotlist item matches an MCP-side safe text search filter.
        /// </summary>
        /// <param name="item">The item to inspect.</param>
        /// <param name="searchText">The optional search text.</param>
        /// <returns><see langword="true" /> when the item should be included.</returns>
        private static bool MatchesSearch(HotlistItemDto item, string? searchText)
        {
            // Search is restricted to safe display fields already supplied by the query layer.
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            return item.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || item.Summary.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || item.RuleCode.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sorts hotlist records deterministically using the requested supported sort field.
        /// </summary>
        /// <param name="records">The records to sort.</param>
        /// <param name="sortBy">The optional sort field.</param>
        /// <returns>A deterministically sorted record array.</returns>
        private static ArchonMcpHotlistFindingRecord[] SortRecords(IEnumerable<ArchonMcpHotlistFindingRecord> records, string? sortBy)
        {
            // All sort paths include stable-key tie breakers so repeated calls return the same order.
            string normalizedSort = NormalizeSortBy(sortBy);
            return normalizedSort switch
            {
                "severity" => records.OrderBy(record => SeverityRank(record.Severity)).ThenBy(record => record.StableKey, StringComparer.Ordinal).ToArray(),
                "latestSeen" => records.OrderByDescending(record => record.LatestSeen ?? DateTimeOffset.MinValue).ThenBy(record => record.StableKey, StringComparer.Ordinal).ToArray(),
                "ruleCode" => records.OrderBy(record => record.RuleCode, StringComparer.Ordinal).ThenBy(record => record.StableKey, StringComparer.Ordinal).ToArray(),
                _ => records.OrderBy(record => record.StableKey, StringComparer.Ordinal).ToArray()
            };
        }

        /// <summary>
        /// Normalizes the requested sort field to a supported deterministic value.
        /// </summary>
        /// <param name="sortBy">The optional requested sort field.</param>
        /// <returns>The normalized sort field.</returns>
        private static string NormalizeSortBy(string? sortBy)
        {
            // Severity is the default because hotlist users usually want the highest-risk findings first.
            return string.IsNullOrWhiteSpace(sortBy) ? "severity" : sortBy.Trim();
        }

        /// <summary>
        /// Assigns a deterministic rank to common severity labels.
        /// </summary>
        /// <param name="severity">The finding severity label.</param>
        /// <returns>A numeric sort rank where lower values represent higher severity.</returns>
        private static int SeverityRank(string severity)
        {
            // Unknown severity labels sort after known high-risk labels but remain deterministic by stable-key tie breaker.
            return severity.ToLowerInvariant() switch
            {
                "critical" => 0,
                "high" => 1,
                "medium" => 2,
                "low" => 3,
                "info" => 4,
                "informational" => 4,
                _ => 5
            };
        }

        /// <summary>
        /// Creates evidence references for returned hotlist findings.
        /// </summary>
        /// <param name="records">The bounded finding records.</param>
        /// <returns>Safe MCP evidence references.</returns>
        private IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Hotlist evidence DTOs expose stable keys and safe display names only, so MCP emits references without snippets.
            return records
                .SelectMany(record => record.EvidenceStableKeys.Select(key => _responseMapper.MapEvidence(key, "FindingEvidence", sourcePath: null, startLine: null, endLine: null, symbolName: null, containingSymbol: null, snippetPreview: null, snippetHash: null, new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Evidence reference was returned by the hotlist query layer."), snapshot: null)))
                .GroupBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates finding references for returned hotlist records.
        /// </summary>
        /// <param name="records">The bounded finding records.</param>
        /// <returns>Safe finding references for the common MCP envelope.</returns>
        private static IReadOnlyList<ArchonMcpFindingReference> CreateFindingReferences(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Finding references summarize stable identities without duplicating every fact in the envelope-level references section.
            return records.Select(record => new ArchonMcpFindingReference(
                    record.StableKey,
                    record.RuleCode,
                    record.RuleVersion,
                    record.Severity,
                    record.Status,
                    new ArchonMcpConfidence(ToConfidenceLevel(record.Confidence), "Finding confidence was supplied by the hotlist query layer."),
                    record.AffectedNodes.Select(node => node.StableKey),
                    record.EvidenceStableKeys))
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknown records for returned hotlist findings.
        /// </summary>
        /// <param name="records">The bounded finding records.</param>
        /// <returns>Unknown records explaining partial or unavailable finding context.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Unknowns preserve partial extraction and missing history context rather than allowing clients to infer certainty.
            List<ArchonMcpUnknown> unknowns = [];
            foreach (ArchonMcpHotlistFindingRecord record in records)
            {
                if (record.FirstSeen is null || record.LatestSeen is null)
                {
                    unknowns.Add(new ArchonMcpUnknown("findingHistoryTimestamps", record.StableKey, $"Finding {record.StableKey} did not include first/latest seen timestamps in the hotlist list response.", "Finding confidence remains based on returned finding data, but history timing conclusions require finding history data.", null));
                }
            }

            return unknowns;
        }

        /// <summary>
        /// Creates warnings for bounded hotlist responses.
        /// </summary>
        /// <param name="limits">The MCP limit metadata produced while mapping records.</param>
        /// <returns>Safe warnings for truncation.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(ArchonMcpLimitMetadata limits)
        {
            // Warnings explain response shaping without exposing finding persistence internals.
            return limits.Truncated
                ? [new ArchonMcpWarning("truncated", "Hotlist finding output was truncated by MCP result limits.", null)]
                : [];
        }

        /// <summary>
        /// Creates safe suggested follow-ups for hotlist investigation.
        /// </summary>
        /// <param name="facts">The mapped hotlist facts.</param>
        /// <param name="limitFollowUps">The shared limit narrowing suggestions.</param>
        /// <returns>Safe follow-up operations or user questions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(ArchonMcpHotlistFindingsFacts facts, IReadOnlyList<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups remain read-only and encourage investigation rather than suppression or mutation.
            List<ArchonMcpSuggestedFollowUp> followUps = [..limitFollowUps];
            if (facts.Findings.Count > 0)
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Assess the impact of the highest-priority finding target.", "archon.assess_change_impact", new Dictionary<string, string> { ["targetStableKey"] = facts.Findings[0].AffectedNodes.FirstOrDefault()?.StableKey ?? facts.Findings[0].StableKey }));
            }

            return followUps;
        }

        /// <summary>
        /// Creates a snapshot identity from the selected hotlist snapshot when available.
        /// </summary>
        /// <param name="request">The original hotlist request.</param>
        /// <param name="records">The bounded returned records.</param>
        /// <returns>A snapshot identity or <see langword="null" /> when no snapshot was known.</returns>
        private static ArchonMcpSnapshotIdentity? CreateSnapshotIdentity(ArchonMcpHotlistFindingsRequest request, IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // Prefer returned finding scope over the request selector because returned stable keys prove the actual persisted snapshot identity.
            string? stableKey = records.FirstOrDefault()?.SnapshotStableKey ?? request.SnapshotSelector;
            return string.IsNullOrWhiteSpace(stableKey)
                ? null
                : new ArchonMcpSnapshotIdentity(stableKey, string.Equals(request.SnapshotSelector, "latest", StringComparison.OrdinalIgnoreCase) ? "latest" : "explicit", "Hotlist findings were scoped to the returned snapshot identity.");
        }

        /// <summary>
        /// Creates an aggregate confidence value for the hotlist response.
        /// </summary>
        /// <param name="records">The bounded returned records.</param>
        /// <returns>A response confidence record.</returns>
        private static ArchonMcpConfidence CreateConfidence(IReadOnlyList<ArchonMcpHotlistFindingRecord> records)
        {
            // The aggregate confidence uses the lowest returned record confidence so the envelope does not overstate certainty.
            if (records.Count == 0)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "No hotlist findings matched the request, so confidence is based on successful query execution rather than returned findings.");
            }

            decimal minimumConfidence = records.Min(record => record.Confidence);
            return new ArchonMcpConfidence(ToConfidenceLevel(minimumConfidence), "Response confidence reflects the lowest confidence among returned hotlist findings.");
        }

        /// <summary>
        /// Maps numeric query confidence to the common MCP confidence level.
        /// </summary>
        /// <param name="confidence">The normalized confidence value.</param>
        /// <returns>The MCP confidence level.</returns>
        private static ArchonMcpConfidenceLevel ToConfidenceLevel(decimal confidence)
        {
            // Thresholds mirror existing MCP tool behavior that treats explicit unknowns and lower scores conservatively.
            return confidence >= 0.85m
                ? ArchonMcpConfidenceLevel.High
                : confidence >= 0.60m
                    ? ArchonMcpConfidenceLevel.Medium
                    : ArchonMcpConfidenceLevel.Low;
        }

        /// <summary>
        /// Creates a grounded summary for the hotlist response.
        /// </summary>
        /// <param name="facts">The mapped hotlist facts.</param>
        /// <returns>A safe natural-language summary.</returns>
        private static string CreateSummary(ArchonMcpHotlistFindingsFacts facts)
        {
            // The summary reports only returned counts and filters, not remediation guidance.
            return facts.Findings.Count == 0
                ? "No hotlist findings matched the supplied filters."
                : $"Returned {facts.Findings.Count} hotlist finding(s) from {facts.TotalMatchingFindings} matching finding(s), sorted by {facts.SortBy}.";
        }

        /// <summary>
        /// Creates safe audit parameters for hotlist requests.
        /// </summary>
        /// <param name="request">The request being audited.</param>
        /// <returns>A safe parameter dictionary for the shared audit sink.</returns>
        private static IReadOnlyDictionary<string, string>? CreateAuditParameters(ArchonMcpHotlistFindingsRequest request)
        {
            // Audit metadata contains only filters and bounds, never raw evidence snippets or suppression intent.
            return new Dictionary<string, string>
            {
                [nameof(request.ProjectStableKey)] = request.ProjectStableKey ?? string.Empty,
                [nameof(request.RuleCode)] = request.RuleCode ?? string.Empty,
                [nameof(request.Category)] = request.Category ?? string.Empty,
                [nameof(request.Severity)] = request.Severity ?? string.Empty,
                [nameof(request.Status)] = request.Status ?? string.Empty,
                [nameof(request.SnapshotSelector)] = request.SnapshotSelector ?? string.Empty,
                [nameof(request.SearchText)] = request.SearchText ?? string.Empty,
                [nameof(request.SortBy)] = request.SortBy ?? string.Empty,
                [nameof(request.Limit)] = request.Limit?.ToString() ?? string.Empty,
                [nameof(request.RepositoryStableKey)] = request.RepositoryStableKey ?? string.Empty,
                [nameof(request.SolutionStableKey)] = request.SolutionStableKey ?? string.Empty
            };
        }

        /// <summary>
        /// Creates a structured validation error for the hotlist operation.
        /// </summary>
        /// <param name="validationResult">The validation failures to expose safely.</param>
        /// <returns>A structured MCP validation response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // Validation details use field names and corrective messages only.
            return ArchonMcpErrorResponse.Create(
                ArchonMcpHotlistOperations.GetHotlistFindings,
                ArchonMcpErrorCategory.Validation,
                string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}")),
                [new ArchonMcpSuggestedFollowUp("Correct hotlist filters and retry finding lookup.", "user.question", null)]);
        }

        /// <summary>
        /// Creates a non-empty filter list for common validation.
        /// </summary>
        /// <param name="values">The optional filter values to include.</param>
        /// <returns>A filter list or <see langword="null" /> when no filters were supplied.</returns>
        private static IReadOnlyList<string>? CreateFilterList(params string?[] values)
        {
            // Common validation only needs supplied filter values, not absent filter fields.
            string[] filters = values.Where(value => value is not null).Select(value => value!).ToArray();
            return filters.Length == 0 ? null : filters;
        }

        /// <summary>
        /// Appends a validation failure when an optional text filter is supplied as whitespace.
        /// </summary>
        /// <param name="failures">The aggregate failure list being built.</param>
        /// <param name="value">The optional filter value to inspect.</param>
        /// <param name="fieldName">The safe request field name used in validation failures.</param>
        private static void AddTextFilterFailure(List<ArchonMcpValidationFailure> failures, string? value, string fieldName)
        {
            // Empty filters are ambiguous and should be corrected before the hotlist query layer runs.
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                failures.Add(new ArchonMcpValidationFailure(fieldName, "Filter value must not be empty when supplied."));
            }
        }
    }
}
