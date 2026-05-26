using Archon.Application.Traversal;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Implements read-only outgoing dependency and incoming dependent MCP traversal tools over the approved graph traversal query abstraction.
    /// </summary>
    public sealed class ArchonMcpDependencyTool : IArchonMcpDependencyTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before traversal logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer traversal execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes bounded graph traversal over persisted architecture snapshots.
        /// </summary>
        private readonly IGraphTraversalQueryService _traversalQueryService;

        /// <summary>
        /// Applies configured MCP response limits to bounded relationship sections.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates a dependency traversal MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="traversalQueryService">The query-layer traversal abstraction used instead of direct graph persistence access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        public ArchonMcpDependencyTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IGraphTraversalQueryService traversalQueryService,
            ArchonMcpLimitGuard limitGuard)
        {
            // Constructor injection keeps traversal testable and prevents the MCP layer from issuing arbitrary Cypher or persistence calls.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _traversalQueryService = traversalQueryService ?? throw new ArgumentNullException(nameof(traversalQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
        }

        /// <inheritdoc />
        public Task<object> GetDependenciesAsync(ArchonMcpDependencyTraversalRequest request, CancellationToken cancellationToken)
        {
            // Outgoing dependency traversal follows source-to-target graph direction.
            return ExecuteTraversalAsync(request, ArchonMcpDependencyOperations.GetDependencies, "Outgoing", cancellationToken);
        }

        /// <inheritdoc />
        public Task<object> GetDependentsAsync(ArchonMcpDependencyTraversalRequest request, CancellationToken cancellationToken)
        {
            // Incoming dependent traversal mirrors dependency traversal so callers can inspect consumers of a stable node.
            return ExecuteTraversalAsync(request, ArchonMcpDependencyOperations.GetDependents, "Incoming", cancellationToken);
        }

        /// <summary>
        /// Executes security, validation, query mapping, and envelope mapping for one dependency traversal operation.
        /// </summary>
        /// <param name="request">The caller-supplied MCP traversal request.</param>
        /// <param name="operationName">The stable MCP operation name being executed.</param>
        /// <param name="direction">The query-layer traversal direction to apply.</param>
        /// <param name="cancellationToken">The token that can cancel traversal execution.</param>
        /// <returns>A traversal envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteTraversalAsync(ArchonMcpDependencyTraversalRequest request, string operationName, string direction, CancellationToken cancellationToken)
        {
            // The operation executor runs first so disabled or unauthorized traversal does not disclose validation or graph-scope details.
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyDictionary<string, string> auditParameters = CreateAuditParameters(request, direction);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                operationName,
                auditParameters,
                () => ExecuteAuthorizedTraversalAsync(request, operationName, direction, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, graph traversal query, and response-envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized MCP traversal request.</param>
        /// <param name="operationName">The stable MCP operation name being executed.</param>
        /// <param name="direction">The query-layer traversal direction to apply.</param>
        /// <param name="cancellationToken">The token that can cancel traversal execution.</param>
        /// <returns>A traversal envelope or structured MCP error response.</returns>
        private async Task<object> ExecuteAuthorizedTraversalAsync(ArchonMcpDependencyTraversalRequest request, string operationName, string direction, CancellationToken cancellationToken)
        {
            // Validation stays inside the authorized delegate to preserve the same fail-closed behavior as other MCP tools.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(operationName, validationResult);
            }

            GraphTraversalQuery query = CreateTraversalQuery(request, direction);
            GraphTraversalResult traversalResult;
            try
            {
                // The query service owns traversal semantics and limit normalization; MCP only maps safe results.
                traversalResult = await _traversalQueryService.TraverseAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation remains visible to the host and should not be serialized as an MCP query failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because traversal dependencies can contain persistence internals.
                return ArchonMcpErrorResponse.Create(
                    operationName,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The graph traversal query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry traversal after verifying graph query data is available.", "user.question", null)]);
            }

            if (!traversalResult.Succeeded)
            {
                return MapTraversalFailure(operationName, traversalResult);
            }

            return MapTraversalSuccess(operationName, request, traversalResult);
        }

        /// <summary>
        /// Validates scope, identity, depth, edge-kind filters, and result limits for traversal.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpDependencyTraversalRequest request)
        {
            // A stable node key is required at MCP level; project name aliases are rejected until the query layer can disambiguate them safely.
            List<ArchonMcpValidationFailure> failures = [];
            string? stableKey = ResolveNodeStableKey(request);
            ArchonMcpValidationRequest validationRequest = new(
                stableKey,
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

            int identityCount = CountSupplied(request.NodeStableKey, request.ProjectStableKey, request.ProjectName);
            if (identityCount == 0)
            {
                failures.Add(new ArchonMcpValidationFailure("nodeIdentity", "A source or target node stable key, project stable key, or unambiguous project identifier is required."));
            }

            if (identityCount > 1)
            {
                failures.Add(new ArchonMcpValidationFailure("nodeIdentity", "Use only one of nodeStableKey, projectStableKey, or projectName for traversal."));
            }

            if (!string.IsNullOrWhiteSpace(request.ProjectName))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.ProjectName), "Project-name traversal requires a stable key in this MCP slice; use archon.describe_project to resolve the project stable key first."));
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
        /// Creates the controlled application-layer traversal query from an MCP request.
        /// </summary>
        /// <param name="request">The validated MCP traversal request.</param>
        /// <param name="direction">The normalized traversal direction to apply.</param>
        /// <returns>A controlled graph traversal query.</returns>
        private static GraphTraversalQuery CreateTraversalQuery(ArchonMcpDependencyTraversalRequest request, string direction)
        {
            // Direct mode is depth one; transitive mode defaults to the query-layer transitive depth unless the caller supplies a stricter depth.
            GraphTraversalSnapshotSelector selector = new(request.RepositoryStableKey, request.SolutionStableKey, request.SnapshotSelector);
            bool transitive = request.Transitive.GetValueOrDefault(false);
            int depth = transitive
                ? request.MaximumDepth.GetValueOrDefault(GraphTraversalLimits.DefaultTransitiveDepth)
                : 1;
            int take = request.Limit.GetValueOrDefault(GraphTraversalLimits.DefaultResultLimit);
            string mode = direction == "Incoming"
                ? transitive ? "TransitiveDependents" : "DirectDependents"
                : transitive ? "TransitiveDependencies" : "DirectDependencies";

            return new GraphTraversalQuery(
                selector,
                ResolveNodeStableKey(request),
                direction,
                depth,
                request.EdgeKindFilters ?? [],
                take,
                mode);
        }

        /// <summary>
        /// Maps a successful graph traversal result into the common MCP envelope.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being mapped.</param>
        /// <param name="request">The original traversal request.</param>
        /// <param name="traversalResult">The successful query-layer traversal result.</param>
        /// <returns>A typed MCP success envelope containing dependency traversal facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts> MapTraversalSuccess(string operationName, ArchonMcpDependencyTraversalRequest request, GraphTraversalResult traversalResult)
        {
            // Successful traversal always includes context and response; validation errors returned before this mapper.
            GraphTraversalResponseDto response = traversalResult.Response ?? throw new InvalidOperationException("Traversal response was not returned for a successful traversal result.");
            GraphTraversalQueryContext context = traversalResult.Context ?? throw new InvalidOperationException("Traversal context was not returned for a successful traversal result.");
            ArchonMcpLimitedList<GraphEdgeDto> limitedEdges = _limitGuard.ApplyResultLimit(response.Edges, request.Limit, operationName);
            ArchonMcpTraversalRelationshipFacts[] relationships = limitedEdges.Items
                .Select(MapRelationship)
                .OrderBy(relationship => relationship.StableKey, StringComparer.Ordinal)
                .ToArray();
            HashSet<string> returnedNodeKeys = relationships
                .SelectMany(relationship => new[] { relationship.SourceNodeStableKey, relationship.TargetNodeStableKey })
                .Append(response.StartNodeStableKey)
                .ToHashSet(StringComparer.Ordinal);
            ArchonMcpTraversalNodeFacts[] nodes = response.Nodes
                .Where(node => returnedNodeKeys.Contains(node.StableKey))
                .Select(MapNode)
                .OrderBy(node => node.StableKey, StringComparer.Ordinal)
                .ToArray();
            ArchonMcpDependencyTraversalFacts facts = new(
                response.StartNodeStableKey,
                response.Direction,
                response.Mode,
                DirectOnly: response.Depth == 1,
                response.Depth,
                response.EdgeKinds,
                DataAvailable: true,
                nodes,
                relationships);
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateEvidenceReferences(nodes, relationships, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUnknowns(nodes, relationships, context, operationName);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateWarnings(context, limitedEdges.Limits, operationName, relationships.Length);
            IReadOnlyList<ArchonMcpSuggestedFollowUp> followUps = CreateFollowUps(operationName, response.StartNodeStableKey, limitedEdges.SuggestedFollowUps);

            return new ArchonMcpEnvelope<ArchonMcpDependencyTraversalFacts>(
                operationName,
                CreateSnapshotIdentity(context),
                CreateSummary(operationName, response, relationships.Length),
                CreateConfidence(unknowns, relationships.Length),
                facts,
                evidence,
                findings: null,
                unknowns,
                warnings,
                limitedEdges.Limits,
                followUps);
        }

        /// <summary>
        /// Maps query-layer traversal failures into safe MCP error responses.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being mapped.</param>
        /// <param name="traversalResult">The failed traversal result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapTraversalFailure(string operationName, GraphTraversalResult traversalResult)
        {
            // Repository/snapshot lookup failures are dependency-unavailable states; missing nodes are not-found states.
            bool unavailable = HasAnyCode(traversalResult, GraphTraversalValidationCodes.RepositoryNotFound, GraphTraversalValidationCodes.SolutionNotFound, GraphTraversalValidationCodes.SnapshotNotFound);
            bool notFound = HasAnyCode(traversalResult, GraphTraversalValidationCodes.NodeNotFound);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : notFound
                    ? ArchonMcpErrorCategory.NotFound
                    : ArchonMcpErrorCategory.Validation;
            string message = unavailable
                ? "Dependency traversal data is unavailable for the requested repository, solution, or snapshot scope."
                : string.Join(" ", traversalResult.ValidationErrors.Select(error => error.Message));

            return ArchonMcpErrorResponse.Create(
                operationName,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check the node, project, repository, and snapshot stable keys before retrying traversal.", "user.question", null)]);
        }

        /// <summary>
        /// Creates a validation error envelope from MCP request validation failures.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being validated.</param>
        /// <param name="validationResult">The validation result produced before query execution.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(string operationName, ArchonMcpValidationResult validationResult)
        {
            // The message references safe field names and bounded validation rules without echoing source snippets or query internals.
            string message = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                operationName,
                ArchonMcpErrorCategory.Validation,
                message,
                [new ArchonMcpSuggestedFollowUp("Correct traversal identity, depth, edge-kind filters, and limits before retrying.", "user.question", null)]);
        }

        /// <summary>
        /// Maps one query-layer node DTO into MCP traversal node facts.
        /// </summary>
        /// <param name="node">The query-layer node DTO.</param>
        /// <returns>The MCP traversal node facts.</returns>
        private static ArchonMcpTraversalNodeFacts MapNode(GraphNodeDto node)
        {
            // Node facts preserve only stable public identities and safe metadata supplied by the query layer.
            return new ArchonMcpTraversalNodeFacts(
                node.StableKey,
                node.Kind,
                node.DisplayName,
                node.ProjectStableKey,
                node.EvidenceStableKeys,
                node.Confidence,
                node.HasUnknownData,
                node.UnknownReason);
        }

        /// <summary>
        /// Maps one query-layer edge DTO into MCP traversal relationship facts.
        /// </summary>
        /// <param name="edge">The query-layer edge DTO.</param>
        /// <returns>The MCP traversal relationship facts.</returns>
        private static ArchonMcpTraversalRelationshipFacts MapRelationship(GraphEdgeDto edge)
        {
            // Relationship facts retain stable edge and endpoint keys while omitting persistence-local identifiers.
            return new ArchonMcpTraversalRelationshipFacts(
                edge.StableKey,
                edge.Kind,
                edge.SourceNodeStableKey,
                edge.TargetNodeStableKey,
                edge.IsDirect,
                edge.EvidenceStableKeys,
                edge.Confidence,
                edge.HasUnknownData,
                edge.UnknownReason);
        }

        /// <summary>
        /// Creates evidence references from traversal node and relationship evidence keys.
        /// </summary>
        /// <param name="nodes">The mapped nodes that may carry evidence stable keys.</param>
        /// <param name="relationships">The mapped relationships that may carry evidence stable keys.</param>
        /// <param name="context">The traversal query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private static IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IEnumerable<ArchonMcpTraversalNodeFacts> nodes, IEnumerable<ArchonMcpTraversalRelationshipFacts> relationships, GraphTraversalQueryContext context)
        {
            // Traversal DTOs expose stable evidence keys only, so references avoid invented paths or source spans.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return nodes.SelectMany(node => node.EvidenceStableKeys.Select(key => new { StableKey = key, Confidence = node.Confidence }))
                .Concat(relationships.SelectMany(relationship => relationship.EvidenceStableKeys.Select(key => new { StableKey = key, Confidence = relationship.Confidence })))
                .GroupBy(item => item.StableKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ArchonMcpEvidenceReference(
                    group.Key,
                    "TraversalEvidenceReference",
                    sourcePath: null,
                    startLine: null,
                    endLine: null,
                    symbolName: null,
                    containingSymbol: null,
                    snippetPreview: null,
                    snippetHash: null,
                    MapConfidence(group.First().Confidence),
                    snapshot))
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknown records from traversal context, nodes, relationships, and empty-result semantics.
        /// </summary>
        /// <param name="nodes">The mapped traversal nodes.</param>
        /// <param name="relationships">The mapped traversal relationships.</param>
        /// <param name="context">The traversal query context containing query-wide unknowns.</param>
        /// <param name="operationName">The stable MCP operation name being mapped.</param>
        /// <returns>Deterministically ordered unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(IReadOnlyList<ArchonMcpTraversalNodeFacts> nodes, IReadOnlyList<ArchonMcpTraversalRelationshipFacts> relationships, GraphTraversalQueryContext context, string operationName)
        {
            // Empty relationships are known absence when traversal succeeded; unavailable data is represented by validation failures before this mapper.
            List<ArchonMcpUnknown> unknowns = [];
            unknowns.AddRange(context.Unknowns.Select(unknown => new ArchonMcpUnknown(
                unknown.Field,
                affectedStableKey: null,
                unknown.Reason,
                "Traversal confidence is reduced because the query layer reported incomplete graph data.",
                new ArchonMcpSuggestedFollowUp("Inspect snapshot extraction diagnostics before drawing dependency conclusions.", "user.question", null))));
            unknowns.AddRange(nodes.Where(node => node.HasUnknownData && !string.IsNullOrWhiteSpace(node.UnknownReason)).Select(node => new ArchonMcpUnknown(
                "traversalNodeUnknownData",
                node.StableKey,
                node.UnknownReason!,
                "A returned traversal node carries unknown-state metadata.",
                new ArchonMcpSuggestedFollowUp("Describe the affected project or symbol for additional context.", "user.question", new Dictionary<string, string> { ["stableKey"] = node.StableKey }))));
            unknowns.AddRange(relationships.Where(relationship => relationship.HasUnknownData && !string.IsNullOrWhiteSpace(relationship.UnknownReason)).Select(relationship => new ArchonMcpUnknown(
                "traversalRelationshipUnknownData",
                relationship.StableKey,
                relationship.UnknownReason!,
                "A returned traversal relationship carries unknown-state metadata.",
                new ArchonMcpSuggestedFollowUp("Inspect related evidence before assuming relationship completeness.", "user.question", new Dictionary<string, string> { ["edgeStableKey"] = relationship.StableKey }))));

            if (relationships.Count == 0)
            {
                unknowns.Add(new ArchonMcpUnknown(
                    operationName == ArchonMcpDependencyOperations.GetDependencies ? "noDependencies" : "noDependents",
                    nodes.FirstOrDefault()?.StableKey,
                    operationName == ArchonMcpDependencyOperations.GetDependencies ? "No dependencies were found within the requested depth and edge-kind bounds." : "No dependents were found within the requested depth and edge-kind bounds.",
                    "This is a known empty traversal result, not an unavailable-data condition.",
                    new ArchonMcpSuggestedFollowUp("Broaden edge-kind filters or increase depth if a wider traversal is appropriate.", "user.question", null)));
            }

            return unknowns
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from traversal context and limit metadata.
        /// </summary>
        /// <param name="context">The traversal query context containing warning DTOs.</param>
        /// <param name="limits">The MCP limit metadata produced while bounding returned relationships.</param>
        /// <param name="operationName">The stable MCP operation name being mapped.</param>
        /// <param name="returnedRelationshipCount">The relationship count returned after MCP limiting.</param>
        /// <returns>Deterministically ordered safe warnings.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(GraphTraversalQueryContext context, ArchonMcpLimitMetadata limits, string operationName, int returnedRelationshipCount)
        {
            // Truncation warnings prevent AI clients from treating bounded traversal as complete graph knowledge.
            List<ArchonMcpWarning> warnings = context.Warnings
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .ToList();
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning($"mcp.{operationName}.truncated", limits.Reason ?? "Traversal relationships were truncated by MCP limits.", affectedStableKey: null));
            }

            if (returnedRelationshipCount == 0)
            {
                warnings.Add(new ArchonMcpWarning($"mcp.{operationName}.empty", "Traversal completed successfully but no matching relationships were found.", affectedStableKey: null));
            }

            return warnings;
        }

        /// <summary>
        /// Creates response-wide suggested follow-ups for dependency traversal.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being mapped.</param>
        /// <param name="startNodeStableKey">The traversal start node stable key.</param>
        /// <param name="limitFollowUps">The follow-ups generated by MCP limit enforcement.</param>
        /// <returns>Deterministically ordered follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(string operationName, string startNodeStableKey, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups stay within read-only MCP investigation operations and reuse stable-key parameters.
            Dictionary<string, string> parameters = new(StringComparer.Ordinal)
            {
                ["nodeStableKey"] = startNodeStableKey
            };
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            followUps.Add(operationName == ArchonMcpDependencyOperations.GetDependencies
                ? new ArchonMcpSuggestedFollowUp("Inspect incoming dependents for the same node.", ArchonMcpDependencyOperations.GetDependents, parameters)
                : new ArchonMcpSuggestedFollowUp("Inspect outgoing dependencies for the same node.", ArchonMcpDependencyOperations.GetDependencies, parameters));
            return followUps
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Operation, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a concise natural-language summary grounded in returned traversal facts.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being mapped.</param>
        /// <param name="response">The query-layer traversal response.</param>
        /// <param name="returnedRelationshipCount">The number of relationship facts returned after limiting.</param>
        /// <returns>A safe traversal summary string.</returns>
        private static string CreateSummary(string operationName, GraphTraversalResponseDto response, int returnedRelationshipCount)
        {
            // Summary text reports only the requested direction, depth, and returned counts without inferring architectural intent.
            string relationshipLabel = operationName == ArchonMcpDependencyOperations.GetDependencies ? "dependencies" : "dependents";
            return $"Traversal found {returnedRelationshipCount} {relationshipLabel} for '{response.StartNodeStableKey}' using {response.Direction} direction to depth {response.Depth}.";
        }

        /// <summary>
        /// Creates overall response confidence from traversal unknowns and relationship count.
        /// </summary>
        /// <param name="unknowns">The explicit unknowns mapped into the response.</param>
        /// <param name="relationshipCount">The returned relationship count after MCP limiting.</param>
        /// <returns>The overall MCP confidence classification.</returns>
        private static ArchonMcpConfidence CreateConfidence(IReadOnlyList<ArchonMcpUnknown> unknowns, int relationshipCount)
        {
            // Empty known traversal results still have medium confidence because they prove bounded absence rather than broad graph completeness.
            if (unknowns.Any(unknown => unknown.Kind is not "noDependencies" and not "noDependents"))
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Traversal completed, but explicit unknowns indicate some graph data may be incomplete.");
            }

            return relationshipCount == 0
                ? new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Traversal completed successfully with no matching relationships in the requested bounds.")
                : new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Persisted traversal facts provide strong support for this response.");
        }

        /// <summary>
        /// Converts numeric query confidence into the common MCP confidence vocabulary.
        /// </summary>
        /// <param name="confidence">The normalized decimal confidence returned by the query layer.</param>
        /// <returns>The MCP confidence classification and rationale.</returns>
        private static ArchonMcpConfidence MapConfidence(decimal confidence)
        {
            // Numeric thresholds mirror other MCP tool mappers so confidence language remains consistent.
            if (confidence >= 0.8m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Persisted traversal evidence provides strong support for this reference.");
            }

            if (confidence >= 0.5m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Persisted traversal evidence provides partial support for this reference.");
            }

            if (confidence > 0m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Low, "Persisted traversal evidence provides limited support for this reference.");
            }

            return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "Traversal confidence was not available in persisted data.");
        }

        /// <summary>
        /// Creates snapshot identity metadata from query-layer traversal context.
        /// </summary>
        /// <param name="context">The traversal context that includes resolved snapshot metadata.</param>
        /// <returns>The MCP snapshot identity record.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(GraphTraversalQueryContext context)
        {
            // Snapshot identity is explicit so clients can cite which graph state produced dependency traversal facts.
            string selectionMode = context.Snapshot.ResolvedAsLatest ? "latest" : "explicit";
            return new ArchonMcpSnapshotIdentity(
                context.Snapshot.SnapshotStableKey,
                selectionMode,
                $"Traversal data resolved for repository '{context.Scope.RepositoryStableKey}' using {selectionMode} snapshot selection.");
        }

        /// <summary>
        /// Resolves the stable traversal node identity from node or project stable-key aliases.
        /// </summary>
        /// <param name="request">The traversal request containing possible identity aliases.</param>
        /// <returns>The stable node key to pass to the query layer, or <see langword="null" /> when none was supplied.</returns>
        private static string? ResolveNodeStableKey(ArchonMcpDependencyTraversalRequest request)
        {
            // Project stable keys are graph node stable keys for project-level traversal, so they can be passed directly to traversal queries.
            return !string.IsNullOrWhiteSpace(request.NodeStableKey)
                ? request.NodeStableKey.Trim()
                : string.IsNullOrWhiteSpace(request.ProjectStableKey) ? null : request.ProjectStableKey.Trim();
        }

        /// <summary>
        /// Counts supplied non-blank identity aliases for traversal validation.
        /// </summary>
        /// <param name="values">The candidate identity alias values.</param>
        /// <returns>The number of non-blank alias values supplied by the caller.</returns>
        private static int CountSupplied(params string?[] values)
        {
            // The count enforces explicit identity choice and avoids selecting arbitrarily between node and project inputs.
            return values.Count(value => !string.IsNullOrWhiteSpace(value));
        }

        /// <summary>
        /// Checks a failed traversal result for any of the supplied validation codes.
        /// </summary>
        /// <param name="traversalResult">The failed traversal result to inspect.</param>
        /// <param name="codes">The validation codes that should match.</param>
        /// <returns><see langword="true" /> when any supplied code is present; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyCode(GraphTraversalResult traversalResult, params string[] codes)
        {
            // String comparer is ordinal because validation codes are stable machine-readable tokens.
            return traversalResult.ValidationErrors.Any(error => codes.Contains(error.Code, StringComparer.Ordinal));
        }

        /// <summary>
        /// Creates safe audit parameters for dependency traversal requests.
        /// </summary>
        /// <param name="request">The request whose non-sensitive fields should be captured for audit.</param>
        /// <param name="direction">The normalized traversal direction being executed.</param>
        /// <returns>Safe request parameters for audit normalization.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(ArchonMcpDependencyTraversalRequest request, string direction)
        {
            // Audit captures traversal scope, shape, and bounds without evidence snippets or graph internals.
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase)
            {
                ["direction"] = direction
            };
            AddIfPresent(parameters, "nodeStableKey", request.NodeStableKey);
            AddIfPresent(parameters, "projectStableKey", request.ProjectStableKey);
            AddIfPresent(parameters, "projectName", request.ProjectName);
            AddIfPresent(parameters, "snapshotSelector", request.SnapshotSelector);
            AddIfPresent(parameters, "repositoryStableKey", request.RepositoryStableKey);
            AddIfPresent(parameters, "solutionStableKey", request.SolutionStableKey);
            if (request.Transitive is not null)
            {
                parameters["transitive"] = request.Transitive.Value.ToString();
            }

            if (request.MaximumDepth is not null)
            {
                parameters["maximumDepth"] = request.MaximumDepth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (request.Limit is not null)
            {
                parameters["limit"] = request.Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (request.EdgeKindFilters is { Count: > 0 })
            {
                parameters["edgeKindFilters"] = string.Join(",", request.EdgeKindFilters);
            }

            return parameters;
        }

        /// <summary>
        /// Adds a non-blank audit parameter value to the dictionary.
        /// </summary>
        /// <param name="parameters">The parameter dictionary being built.</param>
        /// <param name="name">The safe audit parameter name.</param>
        /// <param name="value">The optional parameter value.</param>
        private static void AddIfPresent(IDictionary<string, string> parameters, string name, string? value)
        {
            // Blank values are omitted so audit records focus on supplied traversal shape.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }
    }
}
