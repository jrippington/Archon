using Archon.Application.Traversal;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpImpact
{
    /// <summary>
    /// Implements the read-only MCP change-impact assessment tool over the approved graph traversal query abstraction.
    /// </summary>
    public sealed class ArchonMcpImpactTool : IArchonMcpImpactTool
    {
        /// <summary>
        /// Defines supported target stable-key prefixes for change-impact assessment.
        /// </summary>
        private static readonly string[] s_supportedTargetPrefixes =
        [
            "project://",
            "symbol://",
            "endpoint://",
            "worker://",
            "dataaccess://",
            "integration://",
            "configuration://",
            "config://",
            "rule://",
            "finding://",
            "metric://"
        ];

        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before impact query logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer traversal execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes bounded incoming graph traversal over persisted architecture snapshots.
        /// </summary>
        private readonly IGraphTraversalQueryService _traversalQueryService;

        /// <summary>
        /// Applies configured MCP response limits to impact records.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates a change-impact MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="traversalQueryService">The query-layer traversal abstraction used instead of arbitrary graph queries.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        public ArchonMcpImpactTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IGraphTraversalQueryService traversalQueryService,
            ArchonMcpLimitGuard limitGuard)
        {
            // Constructor injection keeps impact assessment testable and prevents the MCP layer from issuing arbitrary Cypher or persistence calls.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _traversalQueryService = traversalQueryService ?? throw new ArgumentNullException(nameof(traversalQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
        }

        /// <inheritdoc />
        public async Task<object> AssessChangeImpactAsync(ArchonMcpChangeImpactRequest request, CancellationToken cancellationToken)
        {
            // Authorization precedes validation and query execution so disabled or unauthorized impact requests fail closed.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpImpactOperations.AssessChangeImpact,
                CreateAuditParameters(request),
                () => ExecuteAuthorizedAssessChangeImpactAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, incoming traversal, and envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized change-impact request.</param>
        /// <param name="cancellationToken">The token that can cancel traversal execution.</param>
        /// <returns>A change-impact envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedAssessChangeImpactAsync(ArchonMcpChangeImpactRequest request, CancellationToken cancellationToken)
        {
            // Validation stays inside the authorized delegate to preserve the shared MCP fail-closed ordering.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            GraphTraversalResult traversalResult;
            try
            {
                // Incoming traversal reports consumers of the changed target, which is the safest default for impact analysis.
                traversalResult = await _traversalQueryService.TraverseAsync(CreateTraversalQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains visible to the host and should not be converted into a serialized query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because traversal dependencies can contain persistence internals.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpImpactOperations.AssessChangeImpact,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The graph traversal query layer failed before a safe change-impact response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry impact assessment after verifying graph query data is available.", "user.question", null)]);
            }

            if (!traversalResult.Succeeded)
            {
                return MapFailure(traversalResult);
            }

            return MapSuccess(request, traversalResult);
        }

        /// <summary>
        /// Validates supported target stable keys, snapshot scope, edge filters, traversal depth, and result limits.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpChangeImpactRequest request)
        {
            // Common validation handles stable-key syntax, selector shape, edge-filter tokens, count, and depth bounds.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                request.TargetStableKey,
                request.SnapshotSelector,
                SearchText: null,
                request.EdgeKindFilters,
                request.Limit,
                request.MaximumDepth,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            if (string.IsNullOrWhiteSpace(request.TargetStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.TargetStableKey), "A target stable key is required for change-impact assessment."));
            }
            else if (!s_supportedTargetPrefixes.Any(prefix => request.TargetStableKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.TargetStableKey), "Target stable key must identify a supported project, symbol, endpoint, worker, data-access, integration, configuration, rule, finding, or metric node."));
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
        /// Creates a controlled incoming traversal query from a validated MCP impact request.
        /// </summary>
        /// <param name="request">The validated MCP impact request.</param>
        /// <returns>A controlled graph traversal query for the application layer.</returns>
        private static GraphTraversalQuery CreateTraversalQuery(ArchonMcpChangeImpactRequest request)
        {
            // Impact assessment walks incoming relationships because incoming edges represent consumers or dependents of the changed target.
            GraphTraversalSnapshotSelector selector = new(request.RepositoryStableKey, request.SolutionStableKey, request.SnapshotSelector);
            bool includeTransitive = request.IncludeTransitive.GetValueOrDefault(true);
            int depth = includeTransitive ? request.MaximumDepth.GetValueOrDefault(GraphTraversalLimits.DefaultTransitiveDepth) : 1;
            int take = request.Limit.GetValueOrDefault(GraphTraversalLimits.DefaultResultLimit);
            return new GraphTraversalQuery(selector, request.TargetStableKey?.Trim(), "Incoming", depth, request.EdgeKindFilters ?? [], take, "ChangeImpact");
        }

        /// <summary>
        /// Maps query-layer traversal failures into safe MCP error responses.
        /// </summary>
        /// <param name="traversalResult">The failed traversal result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapFailure(GraphTraversalResult traversalResult)
        {
            // Traversal validation codes are mapped to broad MCP categories without exposing graph storage internals.
            bool unavailable = HasAnyCode(traversalResult.ValidationErrors, GraphTraversalValidationCodes.RepositoryNotFound, GraphTraversalValidationCodes.SolutionNotFound, GraphTraversalValidationCodes.SnapshotNotFound);
            bool notFound = HasAnyCode(traversalResult.ValidationErrors, GraphTraversalValidationCodes.NodeNotFound);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : notFound
                    ? ArchonMcpErrorCategory.NotFound
                    : ArchonMcpErrorCategory.Validation;
            string message = unavailable
                ? "Impact graph data is unavailable for the requested repository, solution, or snapshot scope."
                : notFound
                    ? "The requested impact target was not found in the selected snapshot."
                    : string.Join(" ", traversalResult.ValidationErrors.Select(error => error.Message));

            return ArchonMcpErrorResponse.Create(
                ArchonMcpImpactOperations.AssessChangeImpact,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check target, repository, solution, and snapshot stable keys before retrying impact assessment.", "user.question", null)]);
        }

        /// <summary>
        /// Maps a successful incoming traversal result into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original MCP impact request containing caller filters and limits.</param>
        /// <param name="traversalResult">The successful query-layer traversal result.</param>
        /// <returns>A typed MCP success envelope containing impact facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpChangeImpactFacts> MapSuccess(ArchonMcpChangeImpactRequest request, GraphTraversalResult traversalResult)
        {
            // Successful traversal always includes response and context because validation failures return before this mapper.
            GraphTraversalResponseDto response = traversalResult.Response ?? throw new InvalidOperationException("Traversal response was not returned for a successful impact result.");
            GraphTraversalQueryContext context = traversalResult.Context ?? throw new InvalidOperationException("Traversal context was not returned for a successful impact result.");
            ArchonMcpChangeImpactRecord[] records = response.Edges
                .Select(edge => MapImpactRecord(edge, response))
                .OrderBy(record => record.Depth)
                .ThenBy(record => record.ImpactedStableKey, StringComparer.Ordinal)
                .ThenBy(record => record.RelationshipStableKey, StringComparer.Ordinal)
                .ToArray();
            ArchonMcpLimitedList<ArchonMcpChangeImpactRecord> limitedRecords = _limitGuard.ApplyResultLimit(records, request.Limit, ArchonMcpImpactOperations.AssessChangeImpact);
            ArchonMcpChangeImpactFacts facts = new(
                response.StartNodeStableKey,
                response.Depth,
                response.EdgeKinds,
                request.IncludeTransitive.GetValueOrDefault(true),
                response.Edges.Count,
                limitedRecords.Items.Where(record => record.Depth == 1).ToArray(),
                limitedRecords.Items.Where(record => record.Depth > 1).ToArray(),
                "Treat these results as read-only investigation guidance; do not infer automatic remediation or code-change instructions from this response.");
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateEvidenceReferences(limitedRecords.Items, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUnknowns(limitedRecords.Items, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateWarnings(context, limitedRecords.Limits, limitedRecords.Items.Count);

            return new ArchonMcpEnvelope<ArchonMcpChangeImpactFacts>(
                ArchonMcpImpactOperations.AssessChangeImpact,
                CreateSnapshotIdentity(context),
                CreateSummary(facts),
                CreateConfidence(limitedRecords.Items, unknowns),
                facts,
                evidence,
                findings: null,
                unknowns,
                warnings,
                limitedRecords.Limits,
                CreateFollowUps(facts, limitedRecords.SuggestedFollowUps));
        }

        /// <summary>
        /// Maps one incoming traversal edge into an impact record.
        /// </summary>
        /// <param name="edge">The traversal edge that indicates an impact relationship.</param>
        /// <param name="response">The full traversal response containing node metadata.</param>
        /// <returns>The MCP impact record.</returns>
        private static ArchonMcpChangeImpactRecord MapImpactRecord(GraphEdgeDto edge, GraphTraversalResponseDto response)
        {
            // Incoming traversal edges point from impacted consumer to target or intermediate dependency; the source node is therefore the impacted node.
            GraphNodeDto impactedNode = response.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey, edge.SourceNodeStableKey))
                ?? new GraphNodeDto(edge.SourceNodeStableKey, "Unknown", edge.SourceNodeStableKey, ProjectStableKey: null, [], edge.Confidence, true, "Impacted node metadata was not returned with traversal output.");
            int depth = StringComparer.Ordinal.Equals(edge.TargetNodeStableKey, response.StartNodeStableKey) ? 1 : 2;
            IReadOnlyList<string> evidenceKeys = edge.EvidenceStableKeys.Concat(impactedNode.EvidenceStableKeys).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            return new ArchonMcpChangeImpactRecord(
                edge.StableKey,
                edge.Kind,
                impactedNode.StableKey,
                impactedNode.Kind,
                impactedNode.DisplayName,
                impactedNode.ProjectStableKey,
                depth,
                evidenceKeys,
                Math.Min(edge.Confidence, impactedNode.Confidence),
                edge.HasUnknownData || impactedNode.HasUnknownData,
                edge.UnknownReason ?? impactedNode.UnknownReason);
        }

        /// <summary>
        /// Creates evidence references from bounded impact records.
        /// </summary>
        /// <param name="records">The bounded impact records that may carry evidence stable keys.</param>
        /// <param name="context">The traversal query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private static IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IEnumerable<ArchonMcpChangeImpactRecord> records, GraphTraversalQueryContext context)
        {
            // Impact DTOs expose stable evidence keys only, so references avoid invented source ranges or snippets.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return records
                .SelectMany(record => record.EvidenceStableKeys.Select(key => new ArchonMcpEvidenceReference(
                    key,
                    "ImpactEvidence",
                    sourcePath: null,
                    startLine: null,
                    endLine: null,
                    symbolName: record.ImpactedName,
                    containingSymbol: record.ProjectStableKey,
                    snippetPreview: null,
                    snippetHash: null,
                    new ArchonMcpConfidence(ToConfidenceLevel(record.Confidence), "Evidence reference is associated with a persisted impact relationship."),
                    snapshot)))
                .GroupBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(evidence => evidence.StableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknowns from query context and impact-level unknown data.
        /// </summary>
        /// <param name="records">The bounded impact records.</param>
        /// <param name="context">The traversal query context containing query-level unknowns.</param>
        /// <returns>Distinct MCP unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(IEnumerable<ArchonMcpChangeImpactRecord> records, GraphTraversalQueryContext context)
        {
            // Unknowns prevent clients from treating bounded traversal as a complete proof of all possible runtime impacts.
            List<ArchonMcpUnknown> unknowns = context.Unknowns
                .Select(unknown => new ArchonMcpUnknown(unknown.Field, ArchonMcpImpactOperations.AssessChangeImpact, unknown.Reason, "Traversal-level unknowns reduce confidence in complete impact coverage.", null))
                .ToList();
            unknowns.AddRange(records
                .Where(record => record.HasUnknownData)
                .Select(record => new ArchonMcpUnknown(record.ImpactedKind, record.ImpactedStableKey, record.UnknownReason ?? "Impact extraction could not prove every relationship for this node.", "Record-level unknowns reduce confidence for this impact.", null)));

            return unknowns
                .GroupBy(unknown => unknown.Kind + "|" + unknown.AffectedStableKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from traversal context, truncation metadata, and known-empty impact results.
        /// </summary>
        /// <param name="context">The traversal query context containing query-level warnings.</param>
        /// <param name="limits">The applied MCP limit metadata.</param>
        /// <param name="recordCount">The number of bounded impact records returned.</param>
        /// <returns>Safe MCP warning records.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(GraphTraversalQueryContext context, ArchonMcpLimitMetadata limits, int recordCount)
        {
            // Warnings keep truncation and no-impact semantics explicit for AI clients.
            List<ArchonMcpWarning> warnings = context.Warnings
                .Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .ToList();
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.assess_change_impact.truncated", "Change-impact output was truncated by MCP response limits.", affectedStableKey: null));
            }

            if (recordCount == 0)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.assess_change_impact.no_impacts", "No persisted incoming impact relationships matched the requested target and filters.", affectedStableKey: null));
            }

            return warnings
                .GroupBy(warning => warning.Code, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe follow-up operations for continued impact investigation.
        /// </summary>
        /// <param name="facts">The returned impact facts.</param>
        /// <param name="limitFollowUps">The follow-ups emitted by shared limit handling.</param>
        /// <returns>Safe suggested follow-up records.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(ArchonMcpChangeImpactFacts facts, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups intentionally guide further read-only investigation and avoid automatic remediation language.
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            followUps.Add(new ArchonMcpSuggestedFollowUp("Inspect direct dependents of the changed target before planning work.", "archon.get_dependents", new Dictionary<string, string> { ["nodeStableKey"] = facts.TargetStableKey }));
            if (facts.DirectImpacts.Count > 0)
            {
                followUps.Add(new ArchonMcpSuggestedFollowUp("Describe the first directly impacted project or symbol for additional context.", "archon.search", new Dictionary<string, string> { ["searchText"] = facts.DirectImpacts[0].ImpactedStableKey }));
            }

            return followUps;
        }

        /// <summary>
        /// Creates the concise natural-language summary for the impact envelope.
        /// </summary>
        /// <param name="facts">The returned impact facts.</param>
        /// <returns>A grounded summary string.</returns>
        private static string CreateSummary(ArchonMcpChangeImpactFacts facts)
        {
            // The summary reports only returned impact counts and avoids remediation recommendations.
            int totalReturned = facts.DirectImpacts.Count + facts.TransitiveImpacts.Count;
            return totalReturned == 0
                ? $"No persisted incoming impacts were returned for {facts.TargetStableKey} within depth {facts.MaximumDepth}."
                : $"Returned {facts.DirectImpacts.Count} direct and {facts.TransitiveImpacts.Count} transitive persisted impacts for {facts.TargetStableKey}.";
        }

        /// <summary>
        /// Creates response confidence from bounded impact records and unknowns.
        /// </summary>
        /// <param name="records">The bounded impact records.</param>
        /// <param name="unknowns">The explicit unknowns returned with the response.</param>
        /// <returns>The overall MCP confidence classification.</returns>
        private static ArchonMcpConfidence CreateConfidence(IReadOnlyList<ArchonMcpChangeImpactRecord> records, IReadOnlyList<ArchonMcpUnknown> unknowns)
        {
            // Impact confidence is intentionally conservative because traversal cannot prove hidden dynamic runtime dispatch.
            if (records.Count == 0)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "No impact relationships were returned for the requested target.");
            }

            decimal average = records.Average(record => record.Confidence);
            ArchonMcpConfidenceLevel level = unknowns.Count > 0 && average < 0.85m ? ArchonMcpConfidenceLevel.Medium : ToConfidenceLevel(average);
            return new ArchonMcpConfidence(level, "Confidence is derived from persisted impact relationship confidence and explicit traversal unknowns.");
        }

        /// <summary>
        /// Converts a numeric confidence value into an MCP confidence level.
        /// </summary>
        /// <param name="confidence">The normalized confidence value.</param>
        /// <returns>The matching MCP confidence level.</returns>
        private static ArchonMcpConfidenceLevel ToConfidenceLevel(decimal confidence)
        {
            // Thresholds match earlier MCP tool mappers so response confidence is consistent across tool families.
            return confidence >= 0.8m
                ? ArchonMcpConfidenceLevel.High
                : confidence >= 0.5m
                    ? ArchonMcpConfidenceLevel.Medium
                    : confidence > 0m
                        ? ArchonMcpConfidenceLevel.Low
                        : ArchonMcpConfidenceLevel.Unknown;
        }

        /// <summary>
        /// Creates the common MCP snapshot identity from traversal query context.
        /// </summary>
        /// <param name="context">The traversal query context containing resolved snapshot metadata.</param>
        /// <returns>The MCP snapshot identity.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(GraphTraversalQueryContext context)
        {
            // Snapshot identity tells clients which persisted architecture snapshot supports the returned impact facts.
            return new ArchonMcpSnapshotIdentity(
                context.Snapshot.SnapshotStableKey,
                context.Snapshot.Selector,
                context.Snapshot.ResolvedAsLatest ? "Resolved as the latest available architecture snapshot." : "Resolved as the requested architecture snapshot.");
        }

        /// <summary>
        /// Determines whether any validation error has one of the supplied stable validation codes.
        /// </summary>
        /// <param name="errors">The query-layer validation errors to inspect.</param>
        /// <param name="codes">The stable validation codes to match.</param>
        /// <returns><see langword="true" /> when any supplied code is present; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyCode(IEnumerable<GraphTraversalValidationError> errors, params string[] codes)
        {
            // Broad category mapping uses stable validation codes and avoids leaking raw query details.
            HashSet<string> expectedCodes = new(codes, StringComparer.Ordinal);
            return errors.Any(error => expectedCodes.Contains(error.Code));
        }

        /// <summary>
        /// Creates a validation error response for invalid MCP impact input.
        /// </summary>
        /// <param name="validationResult">The validation result containing all request failures.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // Public validation output gives clients corrective guidance without invoking graph traversal dependencies.
            string details = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                ArchonMcpImpactOperations.AssessChangeImpact,
                ArchonMcpErrorCategory.Validation,
                details,
                [new ArchonMcpSuggestedFollowUp("Correct target stable key, scope, depth, edge filters, and limits before retrying impact assessment.", "user.question", null)]);
        }

        /// <summary>
        /// Creates safe audit parameters for a change-impact request.
        /// </summary>
        /// <param name="request">The caller-supplied request.</param>
        /// <returns>Safe normalized audit parameters.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(ArchonMcpChangeImpactRequest request)
        {
            // Audit parameters include only stable keys, controlled filters, and numeric bounds, never evidence snippets or remediation instructions.
            Dictionary<string, string> parameters = new(StringComparer.Ordinal)
            {
                [nameof(request.SnapshotSelector)] = request.SnapshotSelector ?? "latest"
            };
            AddIfPresent(parameters, nameof(request.TargetStableKey), request.TargetStableKey);
            AddIfPresent(parameters, nameof(request.RepositoryStableKey), request.RepositoryStableKey);
            AddIfPresent(parameters, nameof(request.SolutionStableKey), request.SolutionStableKey);
            if (request.EdgeKindFilters is { Count: > 0 })
            {
                parameters[nameof(request.EdgeKindFilters)] = string.Join(",", request.EdgeKindFilters);
            }

            if (request.MaximumDepth is not null)
            {
                parameters[nameof(request.MaximumDepth)] = request.MaximumDepth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (request.Limit is not null)
            {
                parameters[nameof(request.Limit)] = request.Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (request.IncludeTransitive is not null)
            {
                parameters[nameof(request.IncludeTransitive)] = request.IncludeTransitive.Value.ToString();
            }

            return parameters;
        }

        /// <summary>
        /// Adds a trimmed audit parameter when the value is meaningful.
        /// </summary>
        /// <param name="parameters">The audit parameter dictionary being built.</param>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The optional parameter value.</param>
        private static void AddIfPresent(Dictionary<string, string> parameters, string name, string? value)
        {
            // Blank values are omitted so audit records remain concise and deterministic.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }
    }
}
