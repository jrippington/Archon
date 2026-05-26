using Archon.Application.Traversal;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;
using System.Globalization;

namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Implements the read-only <c>archon.find_dependency_paths</c> MCP tool over the approved graph traversal query abstraction.
    /// </summary>
    public sealed class ArchonMcpDependencyPathTool : IArchonMcpDependencyPathTool
    {
        /// <summary>
        /// Executes authorization, allow-listing, and audit behavior before dependency-path logic runs.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Validates common MCP request fields before query-layer path execution.
        /// </summary>
        private readonly IArchonMcpRequestValidator _requestValidator;

        /// <summary>
        /// Executes bounded dependency-path search over persisted architecture snapshots.
        /// </summary>
        private readonly IGraphTraversalQueryService _traversalQueryService;

        /// <summary>
        /// Applies configured MCP response limits to bounded path and evidence sections.
        /// </summary>
        private readonly ArchonMcpLimitGuard _limitGuard;

        /// <summary>
        /// Creates a dependency-path MCP tool handler.
        /// </summary>
        /// <param name="operationExecutor">The executor that performs security, allow-listing, audit, and safe failure mapping.</param>
        /// <param name="requestValidator">The common MCP request validator.</param>
        /// <param name="traversalQueryService">The query-layer traversal abstraction used instead of direct graph persistence access.</param>
        /// <param name="limitGuard">The guard that applies configured MCP result limits.</param>
        public ArchonMcpDependencyPathTool(
            IArchonMcpOperationExecutor operationExecutor,
            IArchonMcpRequestValidator requestValidator,
            IGraphTraversalQueryService traversalQueryService,
            ArchonMcpLimitGuard limitGuard)
        {
            // Constructor injection keeps path search testable and prevents MCP code from issuing arbitrary Cypher or graph-store calls.
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
            _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
            _traversalQueryService = traversalQueryService ?? throw new ArgumentNullException(nameof(traversalQueryService));
            _limitGuard = limitGuard ?? throw new ArgumentNullException(nameof(limitGuard));
        }

        /// <inheritdoc />
        public async Task<object> FindDependencyPathsAsync(ArchonMcpDependencyPathRequest request, CancellationToken cancellationToken)
        {
            // Security runs before validation so disabled or unauthorized path requests reveal no graph-shape details.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpDependencyPathOperation.Name,
                CreateAuditParameters(request),
                () => ExecuteAuthorizedPathSearchAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Executes validation, query mapping, dependency-path search, and MCP envelope mapping after authorization succeeds.
        /// </summary>
        /// <param name="request">The authorized dependency-path MCP request.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer path search.</param>
        /// <returns>A dependency-path success envelope or structured error response.</returns>
        private async Task<object> ExecuteAuthorizedPathSearchAsync(ArchonMcpDependencyPathRequest request, CancellationToken cancellationToken)
        {
            // Validation remains inside the authorized delegate to preserve the fail-closed security order used by every MCP tool.
            ArchonMcpValidationResult validationResult = ValidateRequest(request);
            if (!validationResult.IsValid)
            {
                return CreateValidationError(validationResult);
            }

            DependencyPathResult pathResult;
            try
            {
                // The application query service owns graph search semantics; MCP supplies only controlled identities and bounds.
                pathResult = await _traversalQueryService.GetDependencyPathAsync(CreateQuery(request), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cooperative cancellation should propagate to the host instead of becoming a serialized dependency failure.
                throw;
            }
            catch (Exception)
            {
                // Public errors omit exception details because traversal failures can contain persistence-specific diagnostics.
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpDependencyPathOperation.Name,
                    ArchonMcpErrorCategory.QueryLayerFailure,
                    "The dependency-path query layer failed before a safe response could be produced.",
                    [new ArchonMcpSuggestedFollowUp("Retry path search after verifying graph query data is available.", "user.question", null)]);
            }

            if (!pathResult.Succeeded)
            {
                return MapPathFailure(pathResult);
            }

            return MapPathSuccess(request, pathResult);
        }

        /// <summary>
        /// Validates stable source and target keys, scope selectors, edge filters, depth, and path limits.
        /// </summary>
        /// <param name="request">The request whose fields should be validated.</param>
        /// <returns>A validation result containing every detected failure.</returns>
        private ArchonMcpValidationResult ValidateRequest(ArchonMcpDependencyPathRequest request)
        {
            // Common validation handles stable-key syntax, snapshot selectors, filters, depth, and result count before path-specific identity rules run.
            List<ArchonMcpValidationFailure> failures = [];
            ArchonMcpValidationRequest validationRequest = new(
                request.SourceNodeStableKey,
                request.SnapshotSelector,
                SearchText: null,
                request.EdgeKindFilters,
                request.Limit,
                request.MaximumDepth,
                PageNumber: null,
                PageSize: null);
            failures.AddRange(_requestValidator.Validate(validationRequest).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.TargetNodeStableKey, nameof(request.TargetNodeStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.RepositoryStableKey, nameof(request.RepositoryStableKey)).Failures);
            failures.AddRange(ArchonMcpRequestValidator.ValidateStableKey(request.SolutionStableKey, nameof(request.SolutionStableKey)).Failures);

            if (string.IsNullOrWhiteSpace(request.SourceNodeStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.SourceNodeStableKey), "A source node stable key is required for dependency path search."));
            }

            if (string.IsNullOrWhiteSpace(request.TargetNodeStableKey))
            {
                failures.Add(new ArchonMcpValidationFailure(nameof(request.TargetNodeStableKey), "A target node stable key is required for dependency path search."));
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
        /// Creates the controlled application-layer dependency-path query from a validated MCP request.
        /// </summary>
        /// <param name="request">The validated MCP dependency-path request.</param>
        /// <returns>A dependency-path query for the application layer.</returns>
        private static DependencyPathQuery CreateQuery(ArchonMcpDependencyPathRequest request)
        {
            // Depth defaults to the transitive traversal depth because path search spans multiple graph hops when not explicitly bounded.
            GraphTraversalSnapshotSelector selector = new(request.RepositoryStableKey, request.SolutionStableKey, request.SnapshotSelector);
            int depth = request.MaximumDepth.GetValueOrDefault(GraphTraversalLimits.DefaultTransitiveDepth);
            return new DependencyPathQuery(
                selector,
                request.SourceNodeStableKey?.Trim(),
                request.TargetNodeStableKey?.Trim(),
                depth,
                request.EdgeKindFilters ?? []);
        }

        /// <summary>
        /// Maps a successful dependency-path result into the common MCP envelope.
        /// </summary>
        /// <param name="request">The original request containing MCP path limits.</param>
        /// <param name="pathResult">The successful query-layer path result.</param>
        /// <returns>A typed MCP envelope containing dependency-path facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpDependencyPathFacts> MapPathSuccess(ArchonMcpDependencyPathRequest request, DependencyPathResult pathResult)
        {
            // Mapping is intentionally isolated so unexpected query-shape mismatches still return a safe MCP failure envelope instead of leaking exceptions.
            try
            {
                return MapPathSuccessCore(request, pathResult);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }

        /// <summary>
        /// Performs dependency-path success mapping once the outer mapper has established the exception boundary.
        /// </summary>
        /// <param name="request">The original request containing MCP path limits.</param>
        /// <param name="pathResult">The successful query-layer path result.</param>
        /// <returns>A typed MCP envelope containing dependency-path facts.</returns>
        private ArchonMcpEnvelope<ArchonMcpDependencyPathFacts> MapPathSuccessCore(ArchonMcpDependencyPathRequest request, DependencyPathResult pathResult)
        {
            // Successful path results can represent a found path, a known no-path answer, or unavailable path data with explicit unknowns.
            DependencyPathResponseDto response = pathResult.Response ?? throw new InvalidOperationException("Dependency path response was not returned for a successful path result.");
            GraphTraversalQueryContext context = pathResult.Context ?? throw new InvalidOperationException("Dependency path context was not returned for a successful path result.");
            ArchonMcpDependencyPathRecord[] allPaths = response.PathFound
                ? [CreatePathRecord(response)]
                : [];
            ArchonMcpLimitedList<ArchonMcpDependencyPathRecord> limitedPaths = _limitGuard.ApplyResultLimit(allPaths, request.Limit, ArchonMcpDependencyPathOperation.Name);
            ArchonMcpDependencyPathFacts facts = new(
                response.SourceNodeStableKey,
                response.TargetNodeStableKey,
                response.PathFound,
                DataAvailable: !response.Unavailable,
                response.Depth,
                response.EdgeKinds,
                limitedPaths.Items);
            IReadOnlyList<ArchonMcpEvidenceReference> evidence = CreateEvidenceReferences(limitedPaths.Items, context);
            IReadOnlyList<ArchonMcpUnknown> unknowns = CreateUnknowns(response, context);
            IReadOnlyList<ArchonMcpWarning> warnings = CreateWarnings(response, context, limitedPaths.Limits);
            IReadOnlyList<ArchonMcpSuggestedFollowUp> followUps = CreateFollowUps(response, limitedPaths.SuggestedFollowUps);

            return new ArchonMcpEnvelope<ArchonMcpDependencyPathFacts>(
                ArchonMcpDependencyPathOperation.Name,
                CreateSnapshotIdentity(context),
                CreateSummary(response, limitedPaths.Items.Count),
                CreateConfidence(response, unknowns),
                facts,
                evidence,
                findings: null,
                unknowns,
                warnings,
                limitedPaths.Limits,
                followUps);
        }

        /// <summary>
        /// Maps query-layer validation failures into safe MCP error responses.
        /// </summary>
        /// <param name="pathResult">The failed dependency-path query result.</param>
        /// <returns>A structured MCP error response.</returns>
        private static ArchonMcpErrorResponse MapPathFailure(DependencyPathResult pathResult)
        {
            // Repository/snapshot failures mean data is unavailable; missing source or target nodes are not-found states.
            bool unavailable = HasAnyCode(pathResult, GraphTraversalValidationCodes.RepositoryNotFound, GraphTraversalValidationCodes.SolutionNotFound, GraphTraversalValidationCodes.SnapshotNotFound);
            bool notFound = HasAnyCode(pathResult, GraphTraversalValidationCodes.SourceNodeNotFound, GraphTraversalValidationCodes.TargetNodeNotFound, GraphTraversalValidationCodes.NodeNotFound);
            ArchonMcpErrorCategory category = unavailable
                ? ArchonMcpErrorCategory.DependencyUnavailable
                : notFound
                    ? ArchonMcpErrorCategory.NotFound
                    : ArchonMcpErrorCategory.Validation;
            string message = unavailable
                ? "Dependency path data is unavailable for the requested repository, solution, or snapshot scope."
                : string.Join(" ", pathResult.ValidationErrors.Select(error => error.Message));

            return ArchonMcpErrorResponse.Create(
                ArchonMcpDependencyPathOperation.Name,
                category,
                message,
                [new ArchonMcpSuggestedFollowUp("Check source, target, repository, solution, and snapshot stable keys before retrying path search.", "user.question", null)]);
        }

        /// <summary>
        /// Creates a validation error response from MCP request validation failures.
        /// </summary>
        /// <param name="validationResult">The validation result produced before query execution.</param>
        /// <returns>A structured MCP validation error response.</returns>
        private static ArchonMcpErrorResponse CreateValidationError(ArchonMcpValidationResult validationResult)
        {
            // Validation responses use safe field names and do not echo evidence snippets or query internals.
            string message = string.Join(" ", validationResult.Failures.Select(failure => $"{failure.Field}: {failure.Message}"));
            return ArchonMcpErrorResponse.Create(
                ArchonMcpDependencyPathOperation.Name,
                ArchonMcpErrorCategory.Validation,
                message,
                [new ArchonMcpSuggestedFollowUp("Correct source and target stable keys, depth, edge-kind filters, and limits before retrying.", "user.question", null)]);
        }

        /// <summary>
        /// Creates one path record from the query-layer response.
        /// </summary>
        /// <param name="response">The query-layer path response.</param>
        /// <returns>A deterministic MCP path record.</returns>
        private static ArchonMcpDependencyPathRecord CreatePathRecord(DependencyPathResponseDto response)
        {
            // Current query-layer path search returns the shortest deterministic path; future slices can add more records without changing the facts contract.
            ArchonMcpTraversalNodeFacts[] nodes = response.Nodes
                .Select(MapNode)
                .ToArray();
            ArchonMcpTraversalRelationshipFacts[] edges = response.Edges
                .Select(MapRelationship)
                .ToArray();
            string stableKey = $"path://{Uri.EscapeDataString(response.SourceNodeStableKey)}-to-{Uri.EscapeDataString(response.TargetNodeStableKey)}-{edges.Length}";
            return new ArchonMcpDependencyPathRecord(stableKey, edges.Length, nodes, edges);
        }

        /// <summary>
        /// Maps one query-layer node DTO into MCP traversal node facts.
        /// </summary>
        /// <param name="node">The query-layer node DTO.</param>
        /// <returns>The MCP traversal node facts.</returns>
        private static ArchonMcpTraversalNodeFacts MapNode(GraphNodeDto node)
        {
            // Node facts preserve stable public identities and safe metadata supplied by the query layer.
            return new ArchonMcpTraversalNodeFacts(node.StableKey, node.Kind, node.DisplayName, node.ProjectStableKey, node.EvidenceStableKeys, node.Confidence, node.HasUnknownData, node.UnknownReason);
        }

        /// <summary>
        /// Maps one query-layer edge DTO into MCP traversal relationship facts.
        /// </summary>
        /// <param name="edge">The query-layer edge DTO.</param>
        /// <returns>The MCP traversal relationship facts.</returns>
        private static ArchonMcpTraversalRelationshipFacts MapRelationship(GraphEdgeDto edge)
        {
            // Relationship facts retain stable edge and endpoint keys while omitting persistence-local identifiers.
            return new ArchonMcpTraversalRelationshipFacts(edge.StableKey, edge.Kind, edge.SourceNodeStableKey, edge.TargetNodeStableKey, edge.IsDirect, edge.EvidenceStableKeys, edge.Confidence, edge.HasUnknownData, edge.UnknownReason);
        }

        /// <summary>
        /// Creates evidence references from the bounded returned path records.
        /// </summary>
        /// <param name="paths">The path records whose nodes and edges carry evidence stable keys.</param>
        /// <param name="context">The query context that supplies snapshot identity.</param>
        /// <returns>Deterministically ordered evidence references.</returns>
        private static IReadOnlyList<ArchonMcpEvidenceReference> CreateEvidenceReferences(IEnumerable<ArchonMcpDependencyPathRecord> paths, GraphTraversalQueryContext context)
        {
            // Path DTOs expose evidence as stable keys only, so MCP references avoid invented source locations or snippets.
            ArchonMcpSnapshotIdentity snapshot = CreateSnapshotIdentity(context);
            return paths.SelectMany(path => path.Nodes.SelectMany(node => node.EvidenceStableKeys.Select(key => new { StableKey = key, Confidence = node.Confidence }))
                    .Concat(path.Edges.SelectMany(edge => edge.EvidenceStableKeys.Select(key => new { StableKey = key, Confidence = edge.Confidence }))))
                .GroupBy(item => item.StableKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ArchonMcpEvidenceReference(group.Key, "DependencyPathEvidenceReference", sourcePath: null, startLine: null, endLine: null, symbolName: null, containingSymbol: null, snippetPreview: null, snippetHash: null, MapConfidence(group.First().Confidence), snapshot))
                .ToArray();
        }

        /// <summary>
        /// Creates explicit unknown records from path response semantics and query context unknowns.
        /// </summary>
        /// <param name="response">The dependency-path response.</param>
        /// <param name="context">The query context containing query-wide unknowns.</param>
        /// <returns>Deterministically ordered unknown records.</returns>
        private static IReadOnlyList<ArchonMcpUnknown> CreateUnknowns(DependencyPathResponseDto response, GraphTraversalQueryContext context)
        {
            // No-path and unavailable-data states are explicit so clients do not infer certainty from empty path lists.
            List<ArchonMcpUnknown> unknowns = [];
            unknowns.AddRange(context.Unknowns.Select(unknown => new ArchonMcpUnknown(unknown.Field, affectedStableKey: null, unknown.Reason, "Dependency-path query context reported incomplete graph data.", new ArchonMcpSuggestedFollowUp("Inspect snapshot extraction diagnostics before drawing path conclusions.", "user.question", null))));
            if (!response.PathFound)
            {
                unknowns.Add(new ArchonMcpUnknown(
                    response.Unavailable ? "dependencyPathDataUnavailable" : "noDependencyPath",
                    response.SourceNodeStableKey,
                    response.Reason ?? "No dependency path was returned.",
                    response.Unavailable ? "The query layer could not determine path data availability for this scope." : "Path search completed successfully and found no matching path within the requested bounds.",
                    new ArchonMcpSuggestedFollowUp("Broaden edge-kind filters or increase depth if a wider path search is appropriate.", "user.question", null)));
            }

            return unknowns
                .OrderBy(unknown => unknown.Kind, StringComparer.Ordinal)
                .ThenBy(unknown => unknown.AffectedStableKey, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates safe warnings from path response semantics, query context, and MCP limits.
        /// </summary>
        /// <param name="response">The dependency-path response.</param>
        /// <param name="context">The query context containing warnings.</param>
        /// <param name="limits">The MCP path limit metadata.</param>
        /// <returns>Deterministically ordered safe warnings.</returns>
        private static IReadOnlyList<ArchonMcpWarning> CreateWarnings(DependencyPathResponseDto response, GraphTraversalQueryContext context, ArchonMcpLimitMetadata limits)
        {
            // Warnings describe bounded or incomplete path output without echoing internal graph diagnostics.
            List<ArchonMcpWarning> warnings = context.Warnings
                .OrderBy(warning => warning.Code, StringComparer.Ordinal)
                .Select(warning => new ArchonMcpWarning(warning.Code, warning.Message, affectedStableKey: null))
                .ToList();
            if (limits.Truncated)
            {
                warnings.Add(new ArchonMcpWarning("mcp.archon.find_dependency_paths.truncated", limits.Reason ?? "Dependency paths were truncated by MCP limits.", affectedStableKey: null));
            }

            if (!response.PathFound)
            {
                warnings.Add(new ArchonMcpWarning(response.Unavailable ? "mcp.archon.find_dependency_paths.unavailable" : "mcp.archon.find_dependency_paths.no_path", response.Reason ?? "No dependency path was returned.", response.SourceNodeStableKey));
            }

            return warnings;
        }

        /// <summary>
        /// Creates safe follow-up suggestions for path investigation.
        /// </summary>
        /// <param name="response">The dependency-path response that supplies stable follow-up parameters.</param>
        /// <param name="limitFollowUps">The follow-ups produced by MCP limit enforcement.</param>
        /// <returns>Deterministically ordered follow-up suggestions.</returns>
        private static IReadOnlyList<ArchonMcpSuggestedFollowUp> CreateFollowUps(DependencyPathResponseDto response, IEnumerable<ArchonMcpSuggestedFollowUp> limitFollowUps)
        {
            // Follow-ups remain read-only and reuse stable keys rather than suggesting remediation or mutation.
            List<ArchonMcpSuggestedFollowUp> followUps = [.. limitFollowUps];
            Dictionary<string, string> sourceParameters = new(StringComparer.Ordinal)
            {
                ["nodeStableKey"] = response.SourceNodeStableKey
            };
            Dictionary<string, string> targetParameters = new(StringComparer.Ordinal)
            {
                ["nodeStableKey"] = response.TargetNodeStableKey
            };
            followUps.Add(new ArchonMcpSuggestedFollowUp("Inspect outgoing dependencies from the path source.", ArchonMcpDependencyOperations.GetDependencies, sourceParameters));
            followUps.Add(new ArchonMcpSuggestedFollowUp("Inspect incoming dependents for the path target.", ArchonMcpDependencyOperations.GetDependents, targetParameters));
            return followUps
                .OrderBy(followUp => followUp.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(followUp => followUp.Operation, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Creates a concise summary grounded in returned path facts.
        /// </summary>
        /// <param name="response">The query-layer path response.</param>
        /// <param name="returnedPathCount">The number of path records returned after MCP limiting.</param>
        /// <returns>A safe natural-language path summary.</returns>
        private static string CreateSummary(DependencyPathResponseDto response, int returnedPathCount)
        {
            // Summary text reports path count and bounds only; it does not infer architecture intent or remediation.
            if (response.PathFound)
            {
                return $"Dependency path search found {returnedPathCount} path between '{response.SourceNodeStableKey}' and '{response.TargetNodeStableKey}' to depth {response.Depth}.";
            }

            return response.Unavailable
                ? $"Dependency path data was unavailable between '{response.SourceNodeStableKey}' and '{response.TargetNodeStableKey}'."
                : $"Dependency path search found no path between '{response.SourceNodeStableKey}' and '{response.TargetNodeStableKey}' to depth {response.Depth}.";
        }

        /// <summary>
        /// Creates overall response confidence from path availability and unknown records.
        /// </summary>
        /// <param name="response">The dependency-path response.</param>
        /// <param name="unknowns">The explicit unknowns mapped into the envelope.</param>
        /// <returns>The overall MCP confidence classification.</returns>
        private static ArchonMcpConfidence CreateConfidence(DependencyPathResponseDto response, IReadOnlyList<ArchonMcpUnknown> unknowns)
        {
            // Found paths have high confidence unless unknowns indicate incomplete graph data; no-path answers are bounded and therefore medium confidence.
            if (response.Unavailable)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Low, "Dependency path data was unavailable for this scope.");
            }

            if (unknowns.Any(unknown => unknown.Kind is not "noDependencyPath"))
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Path search completed, but explicit unknowns indicate graph data may be incomplete.");
            }

            return response.PathFound
                ? new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Persisted graph path facts provide strong support for this response.")
                : new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Path search completed successfully with no matching path in the requested bounds.");
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
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Persisted path evidence provides strong support for this reference.");
            }

            if (confidence >= 0.5m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Medium, "Persisted path evidence provides partial support for this reference.");
            }

            if (confidence > 0m)
            {
                return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Low, "Persisted path evidence provides limited support for this reference.");
            }

            return new ArchonMcpConfidence(ArchonMcpConfidenceLevel.Unknown, "Path evidence confidence was not available in persisted data.");
        }

        /// <summary>
        /// Creates snapshot identity metadata from query-layer traversal context.
        /// </summary>
        /// <param name="context">The traversal context that includes resolved snapshot metadata.</param>
        /// <returns>The MCP snapshot identity record.</returns>
        private static ArchonMcpSnapshotIdentity CreateSnapshotIdentity(GraphTraversalQueryContext context)
        {
            // Snapshot identity is explicit so clients can cite which graph state produced dependency-path facts.
            string selectionMode = context.Snapshot.ResolvedAsLatest ? "latest" : "explicit";
            return new ArchonMcpSnapshotIdentity(context.Snapshot.SnapshotStableKey, selectionMode, $"Dependency path data resolved for repository '{context.Scope.RepositoryStableKey}' using {selectionMode} snapshot selection.");
        }

        /// <summary>
        /// Checks a failed path result for any of the supplied validation codes.
        /// </summary>
        /// <param name="pathResult">The failed path result to inspect.</param>
        /// <param name="codes">The validation codes that should match.</param>
        /// <returns><see langword="true" /> when any supplied code is present; otherwise, <see langword="false" />.</returns>
        private static bool HasAnyCode(DependencyPathResult pathResult, params string[] codes)
        {
            // String comparer is ordinal because validation codes are stable machine-readable tokens.
            return pathResult.ValidationErrors.Any(error => codes.Contains(error.Code, StringComparer.Ordinal));
        }

        /// <summary>
        /// Creates safe audit parameters for dependency-path requests.
        /// </summary>
        /// <param name="request">The request whose non-sensitive fields should be captured for audit.</param>
        /// <returns>Safe request parameters for audit normalization.</returns>
        private static IReadOnlyDictionary<string, string> CreateAuditParameters(ArchonMcpDependencyPathRequest request)
        {
            // Audit captures path scope, endpoints, filters, and bounds without evidence snippets or graph internals.
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(parameters, "sourceNodeStableKey", request.SourceNodeStableKey);
            AddIfPresent(parameters, "targetNodeStableKey", request.TargetNodeStableKey);
            AddIfPresent(parameters, "snapshotSelector", request.SnapshotSelector);
            AddIfPresent(parameters, "repositoryStableKey", request.RepositoryStableKey);
            AddIfPresent(parameters, "solutionStableKey", request.SolutionStableKey);
            if (request.MaximumDepth is not null)
            {
                parameters["maximumDepth"] = request.MaximumDepth.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (request.Limit is not null)
            {
                parameters["limit"] = request.Limit.Value.ToString(CultureInfo.InvariantCulture);
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
            // Blank values are omitted so audit records focus on supplied path-search shape.
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }
    }
}
