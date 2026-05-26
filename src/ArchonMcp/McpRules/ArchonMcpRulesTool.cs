using Archon.Application.Rules;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpRules
{
    /// <summary>
    /// Implements the read-only MCP architecture-rule catalog tool over the approved hotlist query abstraction.
    /// </summary>
    public sealed class ArchonMcpRulesTool : IArchonMcpRulesTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before rule catalog query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes bounded rule catalog queries through the application layer.
        /// </summary>
        private readonly IHotlistQueryService _hotlistQueryService;

        /// <summary>
        /// Applies configured MCP response limits to rule catalog records.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates a rule catalog MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="hotlistQueryService">The query-layer rule catalog abstraction used instead of direct graph or filesystem access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        public ArchonMcpRulesTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IHotlistQueryService hotlistQueryService,
            ArchonMcpLimitGuard limitGuard)
        {
            // Constructor injection keeps the tool testable and prevents the MCP layer from bypassing approved query seams.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _hotlistQueryService = hotlistQueryService ?? throw new ArgumentNullException(nameof(hotlistQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
        }

        /// <inheritdoc />
        public async Task<object> GetArchitectureRulesAsync(ArchonMcpArchitectureRulesRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized catalog requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpRulesOperations.GetArchitectureRules,
                CreateAuditParameters(request),
                () => ExecuteAuthorizedGetArchitectureRulesAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, catalog query, and envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized rule catalog request.</param>
        /// <param name="cancellationToken">The token that can cancel catalog query execution.</param>
        /// <returns>A rule catalog envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedGetArchitectureRulesAsync(ArchonMcpArchitectureRulesRequest request, CancellationToken cancellationToken)
        {
            // Validation remains inside the authorized delegate to preserve shared MCP fail-closed ordering.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            PagedQueryResult<RuleCatalogItemDto> page;
            try
            {
                // The application query service owns catalog filtering and prevents MCP from reading rule files or graph internals directly.
                page = await _hotlistQueryService.ListRulesAsync(CreateQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be serialized as a query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because catalog dependencies can contain persistence or adapter internals.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpRulesOperations.GetArchitectureRules,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The architecture-rule catalog query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry rule catalog lookup after verifying rule query data is available.", "user.question", null)]);
            }

            return MapSuccess(request, page);
        }

        /// <summary>
        /// Validates optional rule filters, snapshot selector, and result limit before rule catalog query execution.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpArchitectureRulesRequest request)
        {
            // Common validation handles snapshot selector, simple filter token shape, and result-count bounds.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                StableKey: null,
                request.SnapshotSelector,
                SearchText: null,
                CreateFilterList(request.RuleCode, request.Category, request.Severity),
                request.Limit,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            AddTextFilterFailure(failures, request.RuleCode, nameof(request.RuleCode));
            AddTextFilterFailure(failures, request.Category, nameof(request.Category));
            AddTextFilterFailure(failures, request.Severity, nameof(request.Severity));
            if (request.Limit is < 1)
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.Limit), "Limit must be one or greater when supplied."));
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Creates a controlled application-layer rule catalog query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP rule catalog request.</param>
        /// <returns>A rule catalog query for the application layer.</returns>
        private static RuleCatalogQuery CreateQuery(ArchonMcpArchitectureRulesRequest request)
        {
            // Snapshot selector is intentionally not mapped because the current rule catalog query is global read-only catalog data.
            int take = request.Limit.GetValueOrDefault(RuleCatalogQuery.DefaultPageSize);
            return new RuleCatalogQuery(
                request.RuleCode,
                version: null,
                request.Category,
                request.Severity,
                request.Enabled,
                builtIn: null,
                ownerScope: null,
                skip: 0,
                take);
        }

        /// <summary>
        /// Maps a successful rule catalog page into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP request containing caller filters and limits.</param>
        /// <param name="page">The successful query-layer rule page.</param>
        /// <returns>A typed MCP success envelope containing rule catalog facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpArchitectureRulesFacts> MapSuccess(ArchonMcpArchitectureRulesRequest request, PagedQueryResult<RuleCatalogItemDto> page)
        {
            // Deterministic ordering and MCP limiting keep the rule catalog response stable and bounded for AI clients.
            ArchonMcpArchitectureRuleRecord[] records = page.Items
                .OrderBy(item => item.RuleCode, StringComparer.Ordinal)
                .ThenBy(item => item.Version, StringComparer.Ordinal)
                .Select(MapRecord)
                .ToArray();
            ArchonMcpLimitedList<ArchonMcpArchitectureRuleRecord> limitedRecords = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpRulesOperations.GetArchitectureRules);
            ArchonMcpArchitectureRulesFacts facts = new(
                request.RuleCode,
                request.Category,
                request.Severity,
                request.Enabled,
                page.TotalCount,
                limitedRecords.Items);

            return new ArchonMcpEnvelope<ArchonMcpArchitectureRulesFacts>(
                ArchonMcpRulesOperations.GetArchitectureRules,
                snapshot: null,
                CreateSummary(facts),
                new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Rule catalog records came from the controlled application query layer."),
                facts,
                evidence: null,
                findings: null,
                CreateUnknowns(facts),
                CreateWarnings(limitedRecords.Limits),
                limitedRecords.Limits,
                CreateFollowUps(facts, limitedRecords.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps one application rule catalog item into the MCP record shape.
        /// </summary>
        /// <param name="item">The query-layer catalog item.</param>
        /// <returns>The MCP rule catalog record.</returns>
        private static ArchonMcpArchitectureRuleRecord MapRecord(RuleCatalogItemDto item)
        {
            // Current query DTOs expose safe catalog summaries and tags; related finding counts and source references are explicit unknowns when absent.
            return new ArchonMcpArchitectureRuleRecord(
                item.RuleCode,
                item.Version,
                item.Name,
                item.Category,
                item.Severity,
                item.DefaultStatus,
                item.Enabled,
                item.BuiltIn,
                item.OwnerScope,
                item.Summary,
                item.Tags,
                RelatedFindingCount: null,
                SourceReferences: []);
        }

        /// <summary>
        /// Creates unknown records for rule fields not supplied by the current catalog query seam.
        /// </summary>
        /// <param name="facts">The mapped rule catalog facts.</param>
        /// <returns>Explicit unknown records for unsupported optional catalog details.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(ArchonMcpArchitectureRulesFacts facts)
        {
            // Unknowns prevent AI clients from inventing counts or source locations that the catalog query did not provide.
            return facts.Rules.Count == 0
                ? []
                :
                [
                    new ArchonMcpUnknown("relatedFindingCounts", null, "The current rule catalog query does not include related finding counts.", "Rule catalog confidence remains high, but finding-volume conclusions require a hotlist query.", null),
                    new ArchonMcpUnknown("ruleSourceReferences", null, "The current rule catalog query does not include safe rule source references.", "Rule catalog confidence remains high, but source-location conclusions are not supported by this response.", null)
                ];
        }

        /// <summary>
        /// Creates warnings for bounded or empty rule catalog responses.
        /// </summary>
        /// <param name="limits">The MCP limit metadata produced while mapping records.</param>
        /// <returns>Safe warnings for truncation or empty catalog results.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(ArchonMcpLimitMetadata limits)
        {
            // Warnings explain response shaping without exposing rule persistence internals.
            List<ArchonMcpWarning> warnings = [];
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("truncated", "Rule catalog output was truncated by MCP result limits.", null));
            }

            return warnings;
        }

        /// <summary>
        /// Creates safe suggested follow-ups for architecture-rule catalog investigation.
        /// </summary>
        /// <param name="facts">The mapped rule catalog facts.</param>
        /// <param name="limitFollowUps">The shared limit narrowing suggestions.</param>
        /// <returns>Safe follow-up operations or user questions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(ArchonMcpArchitectureRulesFacts facts, IReadOnlyList<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups stay read-only and point to finding review rather than rule mutation.
            List<ArchonMcpSuggestedFollowUp> followUps = [..limitFollowUps];
            if (facts.Rules.Count > 0)
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Review findings produced by a rule with archon.get_hotlist_findings.", "archon.get_hotlist_findings", new Dictionary<string, string> { ["ruleCode"] = facts.Rules[0].RuleCode }));
            }

            return followUps;
        }

        /// <summary>
        /// Creates a grounded summary for the rule catalog response.
        /// </summary>
        /// <param name="facts">The mapped rule catalog facts.</param>
        /// <returns>A safe natural-language summary.</returns>
        private static string CreateSummary(ArchonMcpArchitectureRulesFacts facts)
        {
            // The summary reports only returned counts and filters, not inferred rule health or enforcement status.
            return facts.Rules.Count == 0
                ? "No architecture rules matched the supplied filters."
                : $"Returned {facts.Rules.Count} architecture rule catalog record(s) from {facts.TotalMatchingRules} matching rule(s).";
        }

        /// <summary>
        /// Creates safe audit parameters for rule catalog requests.
        /// </summary>
        /// <param name="request">The request being audited.</param>
        /// <returns>A safe parameter dictionary for the shared audit sink.</returns>
        private static IReadOnlyDictionary<string, string>? CreateAuditParameters(ArchonMcpArchitectureRulesRequest request)
        {
            // Audit metadata includes only filter values and result bounds, never rule file contents or evidence snippets.
            return new Dictionary<string, string>
            {
                [nameof(request.RuleCode)] = request.RuleCode ?? string.Empty,
                [nameof(request.Category)] = request.Category ?? string.Empty,
                [nameof(request.Severity)] = request.Severity ?? string.Empty,
                [nameof(request.Enabled)] = request.Enabled?.ToString() ?? string.Empty,
                [nameof(request.SnapshotSelector)] = request.SnapshotSelector ?? string.Empty,
                [nameof(request.Limit)] = request.Limit?.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// Creates a structured validation error for the rule catalog operation.
        /// </summary>
        /// <param name="validationResult">The validation failures to expose safely.</param>
        /// <returns>A structured MCP validation response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // Validation details use field names and corrective messages only.
            return ArchonMcpErrorResponse.Create(
                ArchonMcpRulesOperations.GetArchitectureRules,
                ArchonMcpErrorCategory.Validation,
                string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}")),
                [new ArchonMcpSuggestedFollowUp("Correct rule catalog filters and retry architecture rule lookup.", "user.question", null)]);
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
            // Empty filters are ambiguous and should be corrected before the rule query layer runs.
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                failures.Add(new ArchonMcpValidationFailure(fieldName, "Filter value must not be empty when supplied."));
            }
        }
    }
}
