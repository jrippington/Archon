using Archon.Application.Rules;
using Archon.Application.Symbols;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Contains the <c>archon.find_symbol_usages</c> implementation for the symbol MCP tool.
    /// </summary>
    public sealed partial class ArchonMcpSymbolTool
    {
        /// <inheritdoc />
        public async Task<object> FindSymbolUsagesAsync(ArchonMcpFindSymbolUsagesRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized usage requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpSymbolOperations.FindSymbolUsages,
                CreateUsageAuditParameters(request),
                () => ExecuteAuthorizedFindSymbolUsagesAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, symbol usage lookup, filtering, limiting, and envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized symbol usage request.</param>
        /// <param name="cancellationToken">The token that can cancel symbol usage execution.</param>
        /// <returns>A symbol usage envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedFindSymbolUsagesAsync(ArchonMcpFindSymbolUsagesRequest request, CancellationToken cancellationToken)
        {
            // Validation remains inside the authorized delegate to preserve consistent MCP fail-closed ordering.
            ArchonMcpValidationResult validationResult = ValidateUsageRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(ArchonMcpSymbolOperations.FindSymbolUsages, validationResult, "Correct symbol usage identity, filters, depth, and limits before retrying.");
            }

            SymbolUsageResult usageResult;
            try
            {
                // Usage lookup is read-only and flows through the application symbol query abstraction rather than direct source inspection.
                usageResult = await _symbolQueryService.ListSymbolUsagesAsync(CreateUsageQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be converted into a serialized query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because query failures may contain internal extraction or persistence information.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpSymbolOperations.FindSymbolUsages,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The symbol usage query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry usage lookup after verifying symbol query data is available.", "user.question", null)]);
            }

            if (!usageResult.Succeeded)
            {
                return MapUsageFailure(usageResult);
            }

            return MapUsageSuccess(request, usageResult);
        }

        /// <summary>
        /// Validates symbol usage identity, filters, scope, depth, and limit fields.
        /// </summary>
        /// <param name="request">The usage request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateUsageRequest(ArchonMcpFindSymbolUsagesRequest request)
        {
            // The current query seam requires a stable symbol key; exact text lookup is reserved until safe disambiguation can be performed in this handler.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                request.SymbolStableKey,
                request.SnapshotSelector,
                request.SearchText,
                request.UsageKindFilters,
                request.Limit,
                request.MaximumDepth,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.ProjectStableKey, nameof(request.ProjectStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            if (string.IsNullOrWhiteSpace(request.SymbolStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.SymbolStableKey), "A symbol stable key is required for usage lookup in this MCP slice."));
            }

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.SearchText), "Search-text usage lookup requires prior disambiguation; use archon.describe_symbol or archon.search to resolve a stable key first."));
            }

            if (request.MaximumDepth is < 1)
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.MaximumDepth), "Maximum depth must be one or greater when supplied."));
            }

            if (request.Limit is < 1)
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.Limit), "Limit must be one or greater when supplied."));
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Creates the controlled application-layer symbol usage query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP usage request.</param>
        /// <returns>A symbol usage query for the application layer.</returns>
        private static SymbolUsageQuery CreateUsageQuery(ArchonMcpFindSymbolUsagesRequest request)
        {
            // Incoming direction reports callers and references to the requested symbol, which is the safest default for usage investigation.
            SymbolSnapshotSelector selector = new(request.RepositoryStableKey, request.SolutionStableKey, request.SnapshotSelector);
            int take = request.Limit.GetValueOrDefault(SymbolQueryLimits.DefaultTake);
            return new SymbolUsageQuery(selector, request.SymbolStableKey?.Trim(), "Incoming", Skip: 0, take);
        }

        /// <summary>
        /// Maps a successful symbol usage result into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP usage request containing filters and limits.</param>
        /// <param name="usageResult">The successful query-layer usage result.</param>
        /// <returns>A typed MCP success envelope containing usage facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpSymbolUsageFacts> MapUsageSuccess(ArchonMcpFindSymbolUsagesRequest request, SymbolUsageResult usageResult)
        {
            // Query-layer paging and MCP limiting both remain visible so clients understand the returned usage set is bounded.
            PagedQueryResult<SymbolUsageDto> page = usageResult.Page ?? throw new InvalidOperationException("Symbol usage page was not returned for a successful usage result.");
            SymbolQueryContext context = usageResult.Context ?? throw new InvalidOperationException("Symbol usage context was not returned for a successful usage result.");
            ArchonMcpSymbolUsageRecord[] filteredUsages = ApplyUsageFilters(page.Items, request)
                .Select(MapUsageRecord)
                .OrderBy(usage => usage.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(usage => usage.StartLine ?? int.MaxValue)
                .ThenBy(usage => usage.UsageStableKey, StringComparer.Ordinal)
                .ToArray();
            ArchonMcpLimitedList<ArchonMcpSymbolUsageRecord> limitedUsages = _limitGuard.ApplyResultLimit(filteredUsages, request.Limit, ArchonMcpSymbolOperations.FindSymbolUsages);
            ArchonMcpSymbolUsageFacts facts = new(
                request.SymbolStableKey?.Trim() ?? string.Empty,
                request.UsageKindFilters ?? [],
                request.ProjectStableKey,
                request.MaximumDepth,
                page.TotalCount,
                limitedUsages.Items);
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateUsageEvidenceReferences(limitedUsages.Items, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUsageUnknowns(limitedUsages.Items, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateUsageWarnings(context, limitedUsages.Limits, limitedUsages.Items.Count);

            return new ArchonMcpEnvelope<ArchonMcpSymbolUsageFacts>(
                ArchonMcpSymbolOperations.FindSymbolUsages,
                CreateSnapshotIdentity(context),
                CreateUsageSummary(facts),
                CreateConfidence(limitedUsages.Items.Count == 0 ? 0m : limitedUsages.Items.Max(usage => usage.Confidence), unknowns, "Persisted symbol usage facts provide strong support for this response."),
                facts,
                evidence,
                findings: null,
                unknowns,
                warnings,
                limitedUsages.Limits,
                CreateUsageFollowUps(facts, limitedUsages.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps query-layer symbol usage failures into safe MCP error responses.
        /// </summary>
        /// <param name="usageResult">The failed symbol usage result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapUsageFailure(SymbolUsageResult usageResult)
        {
            // Query-layer validation codes are mapped to broad MCP categories without exposing extraction internals.
            bool unavailable = HasAnyCode(usageResult.ValidationErrors, SymbolQueryValidationCodes.RepositoryNotFound, SymbolQueryValidationCodes.SolutionNotFound, SymbolQueryValidationCodes.SnapshotNotFound);
            bool notFound = HasAnyCode(usageResult.ValidationErrors, SymbolQueryValidationCodes.SymbolNotFound);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : notFound
                    ? ArchonMcpErrorCategory.NotFound
                    : ArchonMcpErrorCategory.Validation;
            string message = unavailable
                ? "Symbol usage data is unavailable for the requested repository, solution, or snapshot scope."
                : string.Join(" ", usageResult.ValidationErrors.Select(error => error.Message));

            return ArchonMcpErrorResponse.Create(
                ArchonMcpSymbolOperations.FindSymbolUsages,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check symbol, repository, solution, and snapshot stable keys before retrying usage lookup.", "user.question", null)]);
        }

        /// <summary>
        /// Applies MCP-level usage-kind and project filters to query-layer usage rows.
        /// </summary>
        /// <param name="usages">The usage rows returned by the query layer.</param>
        /// <param name="request">The original MCP usage request containing optional filters.</param>
        /// <returns>The filtered usage rows.</returns>
        private static IEnumerable<SymbolUsageDto> ApplyUsageFilters(IEnumerable<SymbolUsageDto> usages, ArchonMcpFindSymbolUsagesRequest request)
        {
            // Filters are applied over stable query DTO fields and never inspect files directly.
            IEnumerable<SymbolUsageDto> filtered = usages;
            if (request.UsageKindFilters is { Count: > 0 })
            {
                HashSet<string> allowedKinds = new(request.UsageKindFilters, StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(usage => allowedKinds.Contains(usage.UsageKind));
            }

            if (!string.IsNullOrWhiteSpace(request.ProjectStableKey))
            {
                string projectKey = request.ProjectStableKey.Trim();
                filtered = filtered.Where(usage => usage.SourceSymbolStableKey.StartsWith(projectKey, StringComparison.Ordinal) || usage.TargetSymbolStableKey.StartsWith(projectKey, StringComparison.Ordinal));
            }

            return filtered;
        }

        /// <summary>
        /// Maps one query-layer usage DTO into an MCP usage record with redacted snippet text.
        /// </summary>
        /// <param name="usage">The query-layer usage DTO.</param>
        /// <returns>The MCP usage record.</returns>
        private ArchonMcpSymbolUsageRecord MapUsageRecord(SymbolUsageDto usage)
        {
            // Snippet preview text is repository evidence and must be redacted and labeled before returning it to AI clients.
            ArchonMcpUntrustedEvidence untrustedEvidence = _secureEvidenceMapper.CreateUntrustedEvidence(usage.UsageStableKey, usage.UsageKind, usage.SnippetPreview);
            return new ArchonMcpSymbolUsageRecord(
                usage.UsageStableKey,
                usage.UsageKind,
                usage.SourceSymbolStableKey,
                usage.TargetSymbolStableKey,
                usage.SourceName,
                usage.TargetName,
                usage.FilePath,
                usage.StartLine,
                usage.EndLine,
                untrustedEvidence.RedactedContent,
                untrustedEvidence.TrustLabel,
                usage.EvidenceStableKeys,
                usage.Confidence,
                usage.HasUnknownData,
                usage.UnknownReason);
        }

        /// <summary>
        /// Creates evidence references from bounded usage rows.
        /// </summary>
        /// <param name="usages">The bounded usage records that may carry evidence stable keys.</param>
        /// <param name="context">The symbol query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private static IReadOnlyList<ArchonMcpEvidenceReference> CreateUsageEvidenceReferences(IEnumerable<ArchonMcpSymbolUsageRecord> usages, SymbolQueryContext context)
        {
            // Usage DTOs expose stable evidence keys only, so references avoid invented source ranges beyond the usage row itself.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return usages.SelectMany(usage => usage.EvidenceStableKeys.Select(key => new { StableKey = key, Usage = usage }))
                .GroupBy(item => item.StableKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ArchonMcpEvidenceReference(
                    group.Key,
                    "SymbolUsageEvidenceReference",
                    group.First().Usage.FilePath,
                    group.First().Usage.StartLine,
                    group.First().Usage.EndLine,
                    group.First().Usage.TargetName,
                    group.First().Usage.SourceName,
                    group.First().Usage.SnippetPreview,
                    snippetHash: null,
                    MapConfidence(group.First().Usage.Confidence),
                    snapshot))
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknown records from usage context and usage rows.
        /// </summary>
        /// <param name="usages">The bounded usage records returned to the client.</param>
        /// <param name="context">The symbol query context containing query-wide unknowns.</param>
        /// <returns>Deterministically ordered unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUsageUnknowns(IReadOnlyList<ArchonMcpSymbolUsageRecord> usages, SymbolQueryContext context)
        {
            // Empty usage results are known absence within the requested bounds, while query-level unknowns describe incomplete semantic extraction.
            List<ArchonMcpUnknown> unknowns = [];
            unknowns.AddRange(context.Unknowns.Select(unknown => new ArchonMcpUnknown(unknown.Field, affectedStableKey: null, unknown.Reason, "Symbol usage context reported incomplete data for the selected snapshot.", new ArchonMcpSuggestedFollowUp("Inspect snapshot extraction diagnostics before drawing usage conclusions.", "user.question", null))));
            unknowns.AddRange(usages.Where(usage => usage.HasUnknownData && !string.IsNullOrWhiteSpace(usage.UnknownReason)).Select(usage => new ArchonMcpUnknown("symbolUsageUnknownData", usage.UsageStableKey, usage.UnknownReason!, "A returned symbol usage carries unknown-state metadata.", new ArchonMcpSuggestedFollowUp("Inspect usage evidence before assuming semantic completeness.", "user.question", new Dictionary<string, string> { ["usageStableKey"] = usage.UsageStableKey }))));
            if (usages.Count == 0)
            {
                unknowns.Add(new ArchonMcpUnknown("noSymbolUsages", affectedStableKey: null, "No usages were found within the requested filters and limits.", "This is a known empty usage result, not an unavailable-data condition.", new ArchonMcpSuggestedFollowUp("Broaden usage-kind or project filters if wider usage discovery is appropriate.", "user.question", null)));
            }

            return unknowns
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from usage context, MCP limits, and empty-result semantics.
        /// </summary>
        /// <param name="context">The symbol query context containing warning DTOs.</param>
        /// <param name="limits">The MCP limit metadata produced while bounding returned usages.</param>
        /// <param name="returnedUsageCount">The number of usage rows returned after MCP limiting.</param>
        /// <returns>Deterministically ordered safe warning records.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateUsageWarnings(SymbolQueryContext context, ArchonMcpLimitMetadata limits, int returnedUsageCount)
        {
            // Truncation warnings prevent clients from treating bounded usage rows as complete program-wide knowledge.
            List<ArchonMcpWarning> warnings = context.Warnings
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .ToList();
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.find_symbol_usages.truncated", limits.Reason ?? "Symbol usages were truncated by MCP limits.", affectedStableKey: null));
            }

            if (returnedUsageCount == 0)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.find_symbol_usages.empty", "Usage lookup completed successfully but no matching usages were found.", affectedStableKey: null));
            }

            return warnings;
        }

        /// <summary>
        /// Creates response-wide follow-up suggestions for usage investigation.
        /// </summary>
        /// <param name="facts">The usage facts that supply stable follow-up parameters.</param>
        /// <param name="limitFollowUps">The follow-ups generated by MCP limit enforcement.</param>
        /// <returns>Deterministically ordered follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateUsageFollowUps(ArchonMcpSymbolUsageFacts facts, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups remain read-only and use stable symbol identity for further context gathering.
            Dictionary<string, string> symbolParameters = new(StringComparer.Ordinal)
            {
                ["symbolStableKey"] = facts.SymbolStableKey
            };
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            followUps.Add(new ArchonMcpSuggestedFollowUp("Describe the target symbol for context.", ArchonMcpSymbolOperations.DescribeSymbol, symbolParameters));
            followUps.Add(new ArchonMcpSuggestedFollowUp("Search for related symbol and project facts.", "archon.search", new Dictionary<string, string> { ["searchText"] = facts.SymbolStableKey }));
            return followUps
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Operation, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a concise natural-language summary grounded in returned usage facts.
        /// </summary>
        /// <param name="facts">The mapped usage facts.</param>
        /// <returns>A safe usage summary string.</returns>
        private static string CreateUsageSummary(ArchonMcpSymbolUsageFacts facts)
        {
            // Summary text reports only bounded counts and filters without inferring runtime behavior or remediation.
            return $"Symbol usage lookup returned {facts.Usages.Count} usages for '{facts.SymbolStableKey}' from {facts.TotalCount} matching persisted usage records.";
        }

        /// <summary>
        /// Creates safe audit parameters for symbol usage requests.
        /// </summary>
        /// <param name="request">The request whose non-sensitive fields should be captured for audit.</param>
        /// <returns>Safe request parameters for audit normalization.</returns>
        private static IReadOnlyDictionary<string, string> CreateUsageAuditParameters(ArchonMcpFindSymbolUsagesRequest request)
        {
            // Audit captures usage scope, filters, and bounds without source snippets or evidence content.
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(parameters, "symbolStableKey", request.SymbolStableKey);
            AddIfPresent(parameters, "searchText", request.SearchText);
            AddIfPresent(parameters, "projectStableKey", request.ProjectStableKey);
            AddIfPresent(parameters, "maximumDepth", request.MaximumDepth);
            AddIfPresent(parameters, "limit", request.Limit);
            AddIfPresent(parameters, "snapshotSelector", request.SnapshotSelector);
            AddIfPresent(parameters, "repositoryStableKey", request.RepositoryStableKey);
            AddIfPresent(parameters, "solutionStableKey", request.SolutionStableKey);
            if (request.UsageKindFilters is { Count: > 0 })
            {
                parameters["usageKindFilters"] = string.Join(",", request.UsageKindFilters);
            }

            return parameters;
        }
    }
}
