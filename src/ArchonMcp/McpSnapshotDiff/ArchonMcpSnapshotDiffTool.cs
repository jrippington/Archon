using Archon.Application.Diff;
using Archon.Application.Rules;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpSnapshotDiff
{
    /// <summary>
    /// Implements the read-only MCP snapshot diff tool over the approved snapshot diff application abstraction.
    /// </summary>
    public sealed class ArchonMcpSnapshotDiffTool : IArchonMcpSnapshotDiffTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before snapshot diff query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before diff execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes controlled snapshot diff comparisons through the application layer.
        /// </summary>
        private readonly ISnapshotDiffService _snapshotDiffService;

        /// <summary>
        /// Applies configured MCP response limits to diff detail records.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates safe evidence references for diff output.
        /// </summary>
        private readonly IArchonMcpResponseMapper _responseMapper;

        /// <summary>
        /// Creates a snapshot diff MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="snapshotDiffService">The query-layer snapshot diff abstraction used instead of arbitrary graph queries.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        /// <param name="responseMapper">The mapper that creates secret-safe evidence references.</param>
        public ArchonMcpSnapshotDiffTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            ISnapshotDiffService snapshotDiffService,
            ArchonMcpLimitGuard limitGuard,
            IArchonMcpResponseMapper responseMapper)
        {
            // Constructor injection keeps diff behavior testable and prevents MCP handlers from issuing direct graph comparisons.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _snapshotDiffService = snapshotDiffService ?? throw new ArgumentNullException(nameof(snapshotDiffService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
            _responseMapper = responseMapper ?? throw new ArgumentNullException(nameof(responseMapper));
        }

        /// <inheritdoc />
        public async Task<object> GetSnapshotDiffAsync(ArchonMcpSnapshotDiffRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized diff requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpSnapshotDiffOperations.GetSnapshotDiff,
                CreateAuditParameters(request),
                () => ExecuteAuthorizedGetSnapshotDiffAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, snapshot diff query, and envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized snapshot diff request.</param>
        /// <param name="cancellationToken">The token that can cancel snapshot diff execution.</param>
        /// <returns>A snapshot diff envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedGetSnapshotDiffAsync(ArchonMcpSnapshotDiffRequest request, CancellationToken cancellationToken)
        {
            // Validation remains inside the authorized delegate to preserve shared MCP fail-closed ordering.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            SnapshotDiffResult diffResult;
            bool useLatest = request.UseLatestComparableSnapshots.GetValueOrDefault(false);
            try
            {
                // The application diff service owns snapshot compatibility, fingerprint comparison, and latest-to-previous resolution.
                diffResult = useLatest
                    ? await _snapshotDiffService.CompareLatestToPreviousAsync(CreateLatestQuery(request), cancellationToken).ConfigureAwait(false)
                    : await _snapshotDiffService.CompareSnapshotsAsync(CreateExplicitQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains host behavior and should not be converted into a serialized query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because diff dependencies can contain snapshot and persistence internals.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpSnapshotDiffOperations.GetSnapshotDiff,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The snapshot diff query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry snapshot diff after verifying comparable snapshot data is available.", "user.question", null)]);
            }

            if (!diffResult.Succeeded)
            {
                return MapFailure(diffResult);
            }

            return MapSuccess(request, diffResult, useLatest);
        }

        /// <summary>
        /// Validates snapshot identities, implied latest-to-previous scope, filters, and result limits.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpSnapshotDiffRequest request)
        {
            // Common validation handles stable-key shape, filter emptiness, and result-count bounds; tool-specific validation handles mutually exclusive diff modes.
            List<ArchonMcpValidationFailure> failures = [];
            bool useLatest = request.UseLatestComparableSnapshots.GetValueOrDefault(false);
            ArchonMcpValidationRequest validationRequest = new(
                StableKey: null,
                SnapshotSelector: null,
                SearchText: null,
                CreateFilterList(request.Domains, request.ChangeKinds, request.RecordKind, request.Severity),
                request.Limit,
                RequestedDepth: null,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.CurrentSnapshotStableKey, nameof(request.CurrentSnapshotStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.PreviousSnapshotStableKey, nameof(request.PreviousSnapshotStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.ProjectStableKey, nameof(request.ProjectStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.TargetStableKey, nameof(request.TargetStableKey)).Failures);
            AddTextFilterFailure(failures, request.RecordKind, nameof(request.RecordKind));
            AddTextFilterFailure(failures, request.Severity, nameof(request.Severity));
            if (request.Limit is < 1)
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.Limit), "Limit must be one or greater when supplied."));
            }

            if (useLatest)
            {
                ValidateLatestMode(request, failures);
            }
            else
            {
                ValidateExplicitMode(request, failures);
            }

            return new ArchonMcpValidationResult(failures);
        }

        /// <summary>
        /// Validates explicit snapshot comparison mode.
        /// </summary>
        /// <param name="request">The snapshot diff request.</param>
        /// <param name="failures">The aggregate failure list being built.</param>
        private static void ValidateExplicitMode(ArchonMcpSnapshotDiffRequest request, List<ArchonMcpValidationFailure> failures)
        {
            // Explicit comparison requires both stable snapshot identities so MCP never guesses a previous snapshot unless requested.
            if (string.IsNullOrWhiteSpace(request.CurrentSnapshotStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.CurrentSnapshotStableKey), "Current snapshot stable key is required unless UseLatestComparableSnapshots is true."));
            }

            if (string.IsNullOrWhiteSpace(request.PreviousSnapshotStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.PreviousSnapshotStableKey), "Previous snapshot stable key is required unless UseLatestComparableSnapshots is true."));
            }
        }

        /// <summary>
        /// Validates implied latest-to-previous comparison mode.
        /// </summary>
        /// <param name="request">The snapshot diff request.</param>
        /// <param name="failures">The aggregate failure list being built.</param>
        private static void ValidateLatestMode(ArchonMcpSnapshotDiffRequest request, List<ArchonMcpValidationFailure> failures)
        {
            // Latest-to-previous resolution is supported only inside an explicit repository scope.
            if (!string.IsNullOrWhiteSpace(request.CurrentSnapshotStableKey) || !string.IsNullOrWhiteSpace(request.PreviousSnapshotStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.UseLatestComparableSnapshots), "Latest-to-previous mode must not also supply explicit snapshot stable keys."));
            }

            if (string.IsNullOrWhiteSpace(request.RepositoryStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.RepositoryStableKey), "Repository stable key is required when UseLatestComparableSnapshots is true."));
            }
        }

        /// <summary>
        /// Creates a controlled explicit snapshot diff query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP diff request.</param>
        /// <returns>A snapshot diff query for the application layer.</returns>
        private static SnapshotDiffQuery CreateExplicitQuery(ArchonMcpSnapshotDiffRequest request)
        {
            // Details are still bounded by the application query and MCP limit guard; summary counts always remain available.
            int take = request.IncludeDetails.GetValueOrDefault(true)
                ? request.Limit.GetValueOrDefault(QueryPagingOptions.DefaultPageSize)
                : 1;
            return new SnapshotDiffQuery(
                request.CurrentSnapshotStableKey,
                request.PreviousSnapshotStableKey,
                request.Domains,
                request.ChangeKinds,
                request.IncludeUnchangedDetails.GetValueOrDefault(false),
                request.ProjectStableKey,
                request.TargetStableKey,
                request.RecordKind,
                request.Severity,
                skip: 0,
                take);
        }

        /// <summary>
        /// Creates a controlled latest-to-previous snapshot diff query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP diff request.</param>
        /// <returns>A latest-to-previous snapshot diff query for the application layer.</returns>
        private static SnapshotDiffLatestQuery CreateLatestQuery(ArchonMcpSnapshotDiffRequest request)
        {
            // The application service resolves the current and previous snapshots and then reuses explicit comparison behavior.
            int take = request.IncludeDetails.GetValueOrDefault(true)
                ? request.Limit.GetValueOrDefault(QueryPagingOptions.DefaultPageSize)
                : 1;
            return new SnapshotDiffLatestQuery(
                request.RepositoryStableKey,
                request.SolutionStableKey,
                request.Domains,
                request.ChangeKinds,
                request.ProjectStableKey,
                request.TargetStableKey,
                request.RecordKind,
                request.Severity,
                request.IncludeUnchangedDetails.GetValueOrDefault(false),
                skip: 0,
                take);
        }

        /// <summary>
        /// Maps query-layer validation failures into safe structured MCP error responses.
        /// </summary>
        /// <param name="diffResult">The failed diff query result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapFailure(SnapshotDiffResult diffResult)
        {
            // Diff validation codes are mapped to coarse MCP categories without exposing persistence or snapshot internals.
            bool notFound = HasAnyCode(diffResult.ValidationErrors, SnapshotDiffValidationCodes.CurrentSnapshotNotFound, SnapshotDiffValidationCodes.PreviousSnapshotNotFound, SnapshotDiffValidationCodes.PreviousComparableSnapshotNotFound);
            bool unavailable = HasAnyCode(diffResult.ValidationErrors, SnapshotDiffValidationCodes.RepositoryNotFound, SnapshotDiffValidationCodes.SolutionNotFound);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : notFound
                    ? ArchonMcpErrorCategory.NotFound
                    : ArchonMcpErrorCategory.Validation;
            string message = category switch
            {
                ArchonMcpErrorCategory.DependencyUnavailable => "Snapshot diff data is unavailable for the requested repository or solution scope.",
                ArchonMcpErrorCategory.NotFound => "One or more requested comparable snapshots were not found.",
                _ => string.Join(" ", diffResult.ValidationErrors.Select(error => error.Message))
            };

            return ArchonMcpErrorResponse.Create(
                ArchonMcpSnapshotDiffOperations.GetSnapshotDiff,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check snapshot, repository, solution, and diff filters before retrying snapshot comparison.", "user.question", null)]);
        }

        /// <summary>
        /// Maps a successful snapshot diff result into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP request containing caller filters and limits.</param>
        /// <param name="diffResult">The successful query-layer diff result.</param>
        /// <param name="usedLatestMode">Indicates whether latest-to-previous resolution produced this diff.</param>
        /// <returns>A typed MCP success envelope containing snapshot diff facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts> MapSuccess(ArchonMcpSnapshotDiffRequest request, SnapshotDiffResult diffResult, bool usedLatestMode)
        {
            // Summary counts are always mapped; detail rows are included only when requested and then bounded by MCP limits.
            ArchonMcpSnapshotDiffSummaryRecord[] summaries = diffResult.Summaries
                .OrderBy(summary => summary.Domain, StringComparer.Ordinal)
                .Select(summary => new ArchonMcpSnapshotDiffSummaryRecord(summary.Domain, summary.AddedCount, summary.RemovedCount, summary.ChangedCount, summary.UnchangedCount))
                .ToArray();
            ArchonMcpSnapshotDiffDetailRecord[] detailRecords = request.IncludeDetails.GetValueOrDefault(true)
                ? diffResult.Items.OrderBy(item => item.Domain, StringComparer.Ordinal).ThenBy(item => item.StableKey, StringComparer.Ordinal).Select(MapDetailRecord).ToArray()
                : [];
            ArchonMcpLimitedList<ArchonMcpSnapshotDiffDetailRecord> limitedDetails = _limitGuard.ApplyResultLimit(detailRecords, request.IncludeDetails.GetValueOrDefault(true) ? request.Limit : 1, ArchonMcpSnapshotDiffOperations.GetSnapshotDiff);
            bool hasChanges = summaries.Any(summary => summary.AddedCount > 0 || summary.RemovedCount > 0 || summary.ChangedCount > 0);
            ArchonMcpSnapshotDiffFacts facts = new(
                diffResult.CurrentSnapshotStableKey,
                diffResult.PreviousSnapshotStableKey,
                diffResult.ComparisonScope,
                usedLatestMode,
                request.Domains ?? [],
                request.ChangeKinds ?? [],
                diffResult.Truncation.TotalAvailableItems,
                summaries,
                limitedDetails.Items,
                hasChanges);

            return new ArchonMcpEnvelope<ArchonMcpSnapshotDiffFacts>(
                ArchonMcpSnapshotDiffOperations.GetSnapshotDiff,
                new ArchonMcpSnapshotIdentity(diffResult.CurrentSnapshotStableKey, usedLatestMode ? "latest" : "explicit", usedLatestMode ? "Snapshot diff resolved the latest and previous comparable snapshots." : "Snapshot diff used explicit current and previous snapshot stable keys."),
                CreateSummary(facts),
                CreateConfidence(facts),
                facts,
                CreateEvidenceReferences(limitedDetails.Items),
                findings: null,
                CreateUnknowns(limitedDetails.Items),
                CreateWarnings(diffResult, limitedDetails.Limits, facts),
                CreateLimitMetadata(diffResult, limitedDetails.Limits, request.IncludeDetails.GetValueOrDefault(true)),
                CreateFollowUps(facts, limitedDetails.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps one query-layer diff item into the MCP detail record shape.
        /// </summary>
        /// <param name="item">The query-layer diff item.</param>
        /// <returns>The MCP snapshot diff detail record.</returns>
        private static ArchonMcpSnapshotDiffDetailRecord MapDetailRecord(SnapshotDiffItemDto item)
        {
            // Detail rows preserve stable keys and fingerprints without exposing raw graph records.
            return new ArchonMcpSnapshotDiffDetailRecord(
                item.Domain,
                item.ChangeKind,
                item.StableKey,
                item.DisplayName,
                item.Kind,
                item.ProjectStableKey,
                item.TargetStableKeys,
                item.Severity,
                item.PreviousFingerprint,
                item.CurrentFingerprint,
                item.ChangedFields,
                item.EvidenceStableKeys,
                item.HasUnknownData,
                item.UnknownReason);
        }

        /// <summary>
        /// Creates evidence references for returned diff details.
        /// </summary>
        /// <param name="details">The bounded diff details.</param>
        /// <returns>Safe MCP evidence references.</returns>
        private IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IReadOnlyList<ArchonMcpSnapshotDiffDetailRecord> details)
        {
            // Diff details expose stable evidence identities only, so MCP emits references without snippets.
            return details
                .SelectMany(detail => detail.EvidenceStableKeys.Select(key => _responseMapper.MapEvidence(key, "SnapshotDiffEvidence", sourcePath: null, startLine: null, endLine: null, symbolName: null, containingSymbol: null, snippetPreview: null, snippetHash: null, new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Evidence reference was returned by the snapshot diff query layer."), snapshot: null)))
                .GroupBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates unknown records for diff details that carry partial context.
        /// </summary>
        /// <param name="details">The bounded diff details.</param>
        /// <returns>Explicit unknown records for partial diff rows.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(IReadOnlyList<ArchonMcpSnapshotDiffDetailRecord> details)
        {
            // Unknowns preserve row-level uncertainty and prevent clients from treating fingerprints as complete semantic explanations.
            return details
                .Where(detail => detail.HasUnknownData)
                .Select(detail => new ArchonMcpUnknown("snapshotDiffDetail", detail.StableKey, string.IsNullOrWhiteSpace(detail.UnknownReason) ? $"Diff row {detail.StableKey} has unknown data." : detail.UnknownReason!, "Diff confidence is lowered for rows whose compared data is partial or uncertain.", null))
                .ToArray();
        }

        /// <summary>
        /// Creates warnings for snapshot diff truncation and known no-change responses.
        /// </summary>
        /// <param name="diffResult">The successful diff result returned by the query layer.</param>
        /// <param name="limits">The MCP detail limit metadata.</param>
        /// <param name="facts">The mapped diff facts.</param>
        /// <returns>Safe warnings for truncation and no-change state.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(SnapshotDiffResult diffResult, ArchonMcpLimitMetadata limits, ArchonMcpSnapshotDiffFacts facts)
        {
            // Warnings explain response shaping and no-change distinctions without exposing snapshot internals.
            List<ArchonMcpWarning> warnings = [];
            if (diffResult.Truncation.Truncated || limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("truncated", "Snapshot diff details were truncated by query or MCP result limits.", null));
            }

            if (!facts.HasChanges)
            {
                warnings.Add(new ArchonMcpWarning("noChanges", "Compared snapshots produced no added, removed, or changed summary counts for the selected filters.", null));
            }

            return warnings;
        }

        /// <summary>
        /// Combines query-layer and MCP detail-limit metadata for the envelope.
        /// </summary>
        /// <param name="diffResult">The successful diff result returned by the query layer.</param>
        /// <param name="mcpLimits">The MCP detail limit metadata.</param>
        /// <param name="includeDetails">Indicates whether detail rows were requested.</param>
        /// <returns>The limit metadata exposed in the common envelope.</returns>
        private static ArchonMcpLimitMetadata CreateLimitMetadata(SnapshotDiffResult diffResult, ArchonMcpLimitMetadata mcpLimits, bool includeDetails)
        {
            // The common envelope has a single limit section, so it reports truncation when either query or MCP detail limiting occurred.
            return new ArchonMcpLimitMetadata(
                diffResult.Truncation.Truncated || mcpLimits.Truncated,
                includeDetails ? "snapshotDiffDetails" : "snapshotDiffSummaryOnly",
                mcpLimits.AppliedLimit,
                mcpLimits.RequestedLimit,
                diffResult.Truncation.TotalAvailableItems,
                includeDetails ? mcpLimits.ReturnedCount : 0,
                diffResult.Truncation.Truncated || mcpLimits.Truncated ? "Snapshot diff details exceeded query or MCP response limits." : null);
        }

        /// <summary>
        /// Creates safe suggested follow-ups for snapshot diff investigation.
        /// </summary>
        /// <param name="facts">The mapped snapshot diff facts.</param>
        /// <param name="limitFollowUps">The shared limit narrowing suggestions.</param>
        /// <returns>Safe follow-up operations or user questions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(ArchonMcpSnapshotDiffFacts facts, IReadOnlyList<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups remain read-only and direct clients to investigate changed stable targets rather than mutate snapshots.
            List<ArchonMcpSuggestedFollowUp> followUps = [..limitFollowUps];
            ArchonMcpSnapshotDiffDetailRecord? firstDetail = facts.Details.FirstOrDefault();
            if (firstDetail is not null)
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Assess impact of a changed stable target.", "archon.assess_change_impact", new Dictionary<string, string> { ["targetStableKey"] = firstDetail.TargetStableKeys.FirstOrDefault() ?? firstDetail.StableKey }));
            }

            return followUps;
        }

        /// <summary>
        /// Creates an aggregate confidence value for the snapshot diff response.
        /// </summary>
        /// <param name="facts">The mapped diff facts.</param>
        /// <returns>A response confidence record.</returns>
        private static ArchonMcpConfidence CreateConfidence(ArchonMcpSnapshotDiffFacts facts)
        {
            // Diff confidence is high when controlled snapshot comparison succeeds, but lowered when row-level unknowns are present.
            return facts.Details.Any(detail => detail.HasUnknownData)
                ? new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Snapshot comparison succeeded, but one or more diff details include unknown data.")
                : new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Snapshot comparison used controlled stable keys and deterministic fingerprints.");
        }

        /// <summary>
        /// Creates a grounded summary for the snapshot diff response.
        /// </summary>
        /// <param name="facts">The mapped diff facts.</param>
        /// <returns>A safe natural-language summary.</returns>
        private static string CreateSummary(ArchonMcpSnapshotDiffFacts facts)
        {
            // The summary reports counts only and does not infer migration or remediation instructions.
            int added = facts.Summaries.Sum(summary => summary.AddedCount);
            int removed = facts.Summaries.Sum(summary => summary.RemovedCount);
            int changed = facts.Summaries.Sum(summary => summary.ChangedCount);
            return facts.HasChanges
                ? $"Snapshot diff found {added} added, {removed} removed, and {changed} changed record(s) across {facts.Summaries.Count} domain(s)."
                : "Snapshot diff found no added, removed, or changed records for the selected filters.";
        }

        /// <summary>
        /// Creates safe audit parameters for snapshot diff requests.
        /// </summary>
        /// <param name="request">The request being audited.</param>
        /// <returns>A safe parameter dictionary for the shared audit sink.</returns>
        private static IReadOnlyDictionary<string, string>? CreateAuditParameters(ArchonMcpSnapshotDiffRequest request)
        {
            // Audit metadata records only stable keys, filters, and bounds, never snapshot contents or raw graph details.
            return new Dictionary<string, string>
            {
                [nameof(request.CurrentSnapshotStableKey)] = request.CurrentSnapshotStableKey ?? string.Empty,
                [nameof(request.PreviousSnapshotStableKey)] = request.PreviousSnapshotStableKey ?? string.Empty,
                [nameof(request.UseLatestComparableSnapshots)] = request.UseLatestComparableSnapshots?.ToString() ?? string.Empty,
                [nameof(request.RepositoryStableKey)] = request.RepositoryStableKey ?? string.Empty,
                [nameof(request.SolutionStableKey)] = request.SolutionStableKey ?? string.Empty,
                [nameof(request.Domains)] = request.Domains is null ? string.Empty : string.Join(",", request.Domains),
                [nameof(request.ChangeKinds)] = request.ChangeKinds is null ? string.Empty : string.Join(",", request.ChangeKinds),
                [nameof(request.ProjectStableKey)] = request.ProjectStableKey ?? string.Empty,
                [nameof(request.TargetStableKey)] = request.TargetStableKey ?? string.Empty,
                [nameof(request.RecordKind)] = request.RecordKind ?? string.Empty,
                [nameof(request.Severity)] = request.Severity ?? string.Empty,
                [nameof(request.IncludeDetails)] = request.IncludeDetails?.ToString() ?? string.Empty,
                [nameof(request.IncludeUnchangedDetails)] = request.IncludeUnchangedDetails?.ToString() ?? string.Empty,
                [nameof(request.Limit)] = request.Limit?.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// Creates a structured validation error for the snapshot diff operation.
        /// </summary>
        /// <param name="validationResult">The validation failures to expose safely.</param>
        /// <returns>A structured MCP validation response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // Validation details use field names and corrective messages only.
            return ArchonMcpErrorResponse.Create(
                ArchonMcpSnapshotDiffOperations.GetSnapshotDiff,
                ArchonMcpErrorCategory.Validation,
                string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}")),
                [new ArchonMcpSuggestedFollowUp("Correct snapshot diff identities and filters before retrying comparison.", "user.question", null)]);
        }

        /// <summary>
        /// Creates a non-empty filter list for common validation.
        /// </summary>
        /// <param name="domains">The optional domain filters.</param>
        /// <param name="changeKinds">The optional change-kind filters.</param>
        /// <param name="recordKind">The optional record-kind filter.</param>
        /// <param name="severity">The optional severity filter.</param>
        /// <returns>A filter list or <see langword="null" /> when no filters were supplied.</returns>
        private static IReadOnlyList<string>? CreateFilterList(IReadOnlyList<string>? domains, IReadOnlyList<string>? changeKinds, string? recordKind, string? severity)
        {
            // Common validation only needs supplied filter values, not absent filter fields.
            string[] filters = (domains ?? [])
                .Concat(changeKinds ?? [])
                .Concat([recordKind, severity])
                .Where(value => value is not null)
                .Select(value => value!)
                .ToArray();
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
            // Empty filters are ambiguous and should be corrected before the diff query layer runs.
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                failures.Add(new ArchonMcpValidationFailure(fieldName, "Filter value must not be empty when supplied."));
            }
        }

        /// <summary>
        /// Determines whether any validation error has one of the supplied deterministic codes.
        /// </summary>
        /// <param name="errors">The validation errors to inspect.</param>
        /// <param name="codes">The validation codes to match.</param>
        /// <returns><see langword="true" /> when any error code matches.</returns>
        private static bool HasAnyCode(IEnumerable<SnapshotDiffValidationError> errors, params string[] codes)
        {
            // Code matching keeps failure category mapping stable even when public messages change.
            return errors.Any(error => codes.Contains(error.Code, StringComparer.Ordinal));
        }
    }
}
