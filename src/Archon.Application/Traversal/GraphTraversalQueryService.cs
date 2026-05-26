using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Projects;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Traversal
{
    /// <summary>
    /// Implements controlled bounded dependency traversal and dependency-path query behavior over extracted architecture snapshots.
    /// </summary>
    public sealed class GraphTraversalQueryService : IGraphTraversalQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Defines the dependency-like edge kinds used when callers do not supply an explicit edge-kind filter.
        /// </summary>
        private static readonly string[] s_defaultDependencyEdgeKinds =
        [
            EdgeKind.References.Value,
            EdgeKind.UsesPackage.Value,
            EdgeKind.DependsOn.Value,
            EdgeKind.CallsExternalService.Value,
            EdgeKind.CallsApi.Value,
            EdgeKind.UsesDbContext.Value,
            EdgeKind.UsesLinqToSqlContext.Value,
            EdgeKind.ReadsTable.Value,
            EdgeKind.WritesTable.Value,
            EdgeKind.CallsStoredProcedure.Value,
            EdgeKind.ExecutesRawSql.Value,
            EdgeKind.Injects.Value,
            EdgeKind.Calls.Value
        ];

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphTraversalQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public GraphTraversalQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // The current query slice reads snapshot contracts through the same in-memory seam used by earlier WP014 endpoints.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <inheritdoc />
        public Task<GraphTraversalResult> TraverseAsync(GraphTraversalQuery query, CancellationToken cancellationToken)
        {
            // Traversal validates scope and limits, resolves a single snapshot, then explores only the bounded reachable edge set.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new GraphTraversalResult(resolution.ValidationErrors));
            }

            TraversalOptionsResult options = NormalizeTraversalOptions(query.Direction, query.Depth, query.Take, query.EdgeKinds, requireNode: true, query.NodeStableKey);
            if (!options.Succeeded)
            {
                return Task.FromResult(new GraphTraversalResult(options.ValidationErrors));
            }

            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            if (!NodeExists(snapshot, query.NodeStableKey!))
            {
                GraphTraversalValidationError error = new(GraphTraversalValidationCodes.NodeNotFound, "The requested traversal start node was not found in the selected snapshot.");
                return Task.FromResult(new GraphTraversalResult([error]));
            }

            GraphTraversalQueryContext context = BuildContext(query.Selector, resolution);
            TraversalWorkResult work = Explore(snapshot, query.NodeStableKey!, options.Direction!, options.Depth, options.EdgeKinds, options.Take, cancellationToken);
            GraphTraversalResponseDto response = BuildTraversalResponse(snapshot, query.NodeStableKey!, query.Mode, options.Direction!, options.Depth, options.EdgeKinds, work);
            GraphTraversalQueryContext responseContext = MergeTruncationContext(context, work.Truncation);
            return Task.FromResult(new GraphTraversalResult(response, responseContext));
        }

        /// <inheritdoc />
        public Task<DependencyPathResult> GetDependencyPathAsync(DependencyPathQuery query, CancellationToken cancellationToken)
        {
            // Path search treats no-path as successful data so clients can distinguish no relationship from malformed input.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotResolution resolution = ResolveSnapshot(query.Selector);
            if (!resolution.Succeeded)
            {
                return Task.FromResult(new DependencyPathResult(resolution.ValidationErrors));
            }

            List<GraphTraversalValidationError> identityErrors = ValidatePathIdentities(query);
            if (identityErrors.Count > 0)
            {
                return Task.FromResult(new DependencyPathResult(identityErrors));
            }

            TraversalOptionsResult options = NormalizeTraversalOptions("Outgoing", query.Depth, GraphTraversalLimits.MaximumResultLimit, query.EdgeKinds, requireNode: false, nodeStableKey: null);
            if (!options.Succeeded)
            {
                return Task.FromResult(new DependencyPathResult(options.ValidationErrors));
            }

            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            List<GraphTraversalValidationError> existenceErrors = ValidatePathNodeExistence(snapshot, query);
            if (existenceErrors.Count > 0)
            {
                return Task.FromResult(new DependencyPathResult(existenceErrors));
            }

            GraphTraversalQueryContext context = BuildContext(query.Selector, resolution);
            DependencyPathResponseDto response = BuildDependencyPathResponse(snapshot, query.SourceNodeStableKey!, query.TargetNodeStableKey!, options.Depth, options.EdgeKinds, cancellationToken);
            GraphTraversalQueryContext responseContext = response.Unavailable
                ? MergeUnknownContext(context, new GraphTraversalUnknownDto("dependencyPath", response.Reason ?? "Dependency path data is unavailable."))
                : context;
            return Task.FromResult(new DependencyPathResult(response, responseContext));
        }

        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when that diagnostic path is available.
        /// </summary>
        /// <returns>The snapshots available to application-layer query services.</returns>
        private IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshots()
        {
            // Infrastructure-backed stores can replace this service later; the current slice uses the repository-standard in-memory query seam.
            return _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics()
                : [];
        }

        /// <summary>
        /// Resolves and validates the selected traversal snapshot scope.
        /// </summary>
        /// <param name="selector">The repository, solution, and snapshot selector supplied by the query.</param>
        /// <returns>A successful snapshot resolution or deterministic validation errors.</returns>
        private SnapshotResolution ResolveSnapshot(GraphTraversalSnapshotSelector selector)
        {
            // Scope validation runs before graph matching so missing or malformed selectors produce client-correctable problem details.
            List<GraphTraversalValidationError> validationErrors = ValidateSelector(selector);
            if (validationErrors.Count > 0)
            {
                return SnapshotResolution.Failed(validationErrors);
            }

            ExtractedArchitectureSnapshot[] repositorySnapshots = GetSnapshots()
                .Where(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.RepositoryStableKey.Value, selector.RepositoryStableKey))
                .ToArray();
            if (repositorySnapshots.Length == 0)
            {
                GraphTraversalValidationError error = new(GraphTraversalValidationCodes.RepositoryNotFound, "The requested repository scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot[] scopedSnapshots = ApplySolutionScope(repositorySnapshots, selector);
            if (selector.SolutionStableKey is not null && scopedSnapshots.Length == 0)
            {
                GraphTraversalValidationError error = new(GraphTraversalValidationCodes.SolutionNotFound, "The requested solution scope was not found for the repository.");
                return SnapshotResolution.Failed([error]);
            }

            ExtractedArchitectureSnapshot? selectedSnapshot = ResolveSelectedSnapshot(scopedSnapshots, selector);
            if (selectedSnapshot?.SnapshotHeader is null)
            {
                GraphTraversalValidationError error = new(GraphTraversalValidationCodes.SnapshotNotFound, "The requested snapshot scope was not found.");
                return SnapshotResolution.Failed([error]);
            }

            return SnapshotResolution.Success(selectedSnapshot, scopedSnapshots);
        }

        /// <summary>
        /// Validates traversal selector syntax before any snapshot matching occurs.
        /// </summary>
        /// <param name="selector">The caller-supplied traversal snapshot selector.</param>
        /// <returns>A deterministic list of selector validation errors.</returns>
        private static List<GraphTraversalValidationError> ValidateSelector(GraphTraversalSnapshotSelector selector)
        {
            // Repository scope is required because latest resolution must be bounded to one repository.
            List<GraphTraversalValidationError> errors = [];
            if (selector.RepositoryStableKey is null)
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.RepositoryStableKeyRequired, "A repository stable key is required for graph traversal queries."));
            }

            if (!selector.RequestsLatestSnapshot && !selector.SnapshotStableKey.StartsWith("snapshot://", StringComparison.Ordinal))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.SnapshotSelectorInvalid, "Snapshot selector must be 'latest', 'current', or a snapshot:// stable key."));
            }

            return errors;
        }

        /// <summary>
        /// Applies the optional solution scope to repository snapshots.
        /// </summary>
        /// <param name="repositorySnapshots">The snapshots already matched to the requested repository.</param>
        /// <param name="selector">The caller-supplied traversal snapshot selector.</param>
        /// <returns>The snapshots matching the optional solution scope.</returns>
        private static ExtractedArchitectureSnapshot[] ApplySolutionScope(IEnumerable<ExtractedArchitectureSnapshot> repositorySnapshots, GraphTraversalSnapshotSelector selector)
        {
            // Solution scope is resolved through snapshot-level solution facts just like project query scope resolution.
            return selector.SolutionStableKey is null
                ? repositorySnapshots.ToArray()
                : repositorySnapshots
                    .Where(snapshot => snapshot.Solutions.Any(solution => StringComparer.Ordinal.Equals(solution.StableKey.Value, selector.SolutionStableKey)))
                    .ToArray();
        }

        /// <summary>
        /// Resolves the selected snapshot from an already scoped snapshot set.
        /// </summary>
        /// <param name="scopedSnapshots">The repository and solution scoped snapshots.</param>
        /// <param name="selector">The caller-supplied traversal snapshot selector.</param>
        /// <returns>The selected snapshot, or null when none matches.</returns>
        private static ExtractedArchitectureSnapshot? ResolveSelectedSnapshot(IEnumerable<ExtractedArchitectureSnapshot> scopedSnapshots, GraphTraversalSnapshotSelector selector)
        {
            // Latest resolution uses completed time, started time, then stable key so repeated calls remain deterministic.
            return selector.RequestsLatestSnapshot
                ? scopedSnapshots
                    .Where(static snapshot => snapshot.SnapshotHeader is not null)
                    .OrderByDescending(static snapshot => snapshot.SnapshotHeader!.CompletedUtc ?? snapshot.SnapshotHeader.StartedUtc)
                    .ThenByDescending(static snapshot => snapshot.SnapshotHeader!.StartedUtc)
                    .ThenByDescending(static snapshot => snapshot.SnapshotHeader!.StableKey.Value, StringComparer.Ordinal)
                    .FirstOrDefault()
                : scopedSnapshots.FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, selector.SnapshotStableKey));
        }

        /// <summary>
        /// Normalizes traversal direction, depth, edge-kind filters, and result limits into safe bounded options.
        /// </summary>
        /// <param name="direction">The optional caller-supplied direction value.</param>
        /// <param name="depth">The requested maximum traversal depth.</param>
        /// <param name="take">The requested maximum returned edge count.</param>
        /// <param name="edgeKinds">The optional caller-supplied edge-kind filter values.</param>
        /// <param name="requireNode">Indicates whether a start-node stable key must be validated.</param>
        /// <param name="nodeStableKey">The optional start-node stable key.</param>
        /// <returns>A successful normalized option set or deterministic validation errors.</returns>
        private static TraversalOptionsResult NormalizeTraversalOptions(string? direction, int depth, int take, IReadOnlyList<string> edgeKinds, bool requireNode, string? nodeStableKey)
        {
            // Option validation is deterministic and happens before graph exploration so malformed automation can repair requests safely.
            List<GraphTraversalValidationError> errors = [];
            if (requireNode && string.IsNullOrWhiteSpace(nodeStableKey))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.NodeStableKeyRequired, "A node stable key is required for graph traversal."));
            }

            string normalizedDirection = string.IsNullOrWhiteSpace(direction) ? "Outgoing" : direction.Trim();
            if (!IsSupportedDirection(normalizedDirection))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.DirectionUnsupported, "Traversal direction must be Outgoing, Incoming, or Both."));
            }

            if (depth < 1 || depth > GraphTraversalLimits.MaximumDepth)
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.DepthInvalid, $"Traversal depth must be between 1 and {GraphTraversalLimits.MaximumDepth}."));
            }

            if (take < 1 || take > GraphTraversalLimits.MaximumResultLimit)
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.TakeInvalid, $"Traversal take must be between 1 and {GraphTraversalLimits.MaximumResultLimit}."));
            }

            string[] normalizedEdgeKinds = NormalizeEdgeKinds(edgeKinds, errors);
            return errors.Count == 0
                ? TraversalOptionsResult.Success(NormalizeDirectionName(normalizedDirection), depth, take, normalizedEdgeKinds)
                : TraversalOptionsResult.Failed(errors);
        }

        /// <summary>
        /// Validates dependency-path source and target identities before graph search starts.
        /// </summary>
        /// <param name="query">The dependency-path query supplied by the caller.</param>
        /// <returns>A deterministic list of path identity validation errors.</returns>
        private static List<GraphTraversalValidationError> ValidatePathIdentities(DependencyPathQuery query)
        {
            // Source and target are separate validation fields so clients can correct either side independently.
            List<GraphTraversalValidationError> errors = [];
            if (string.IsNullOrWhiteSpace(query.SourceNodeStableKey))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.SourceNodeStableKeyRequired, "A source node stable key is required for dependency path queries."));
            }

            if (string.IsNullOrWhiteSpace(query.TargetNodeStableKey))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.TargetNodeStableKeyRequired, "A target node stable key is required for dependency path queries."));
            }

            return errors;
        }

        /// <summary>
        /// Validates that dependency-path source and target nodes exist in the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected snapshot that scopes path search.</param>
        /// <param name="query">The dependency-path query supplied by the caller.</param>
        /// <returns>A deterministic list of path node existence validation errors.</returns>
        private static List<GraphTraversalValidationError> ValidatePathNodeExistence(ExtractedArchitectureSnapshot snapshot, DependencyPathQuery query)
        {
            // Missing endpoints are validation failures, while a valid pair with no relationship becomes a normal no-path payload.
            List<GraphTraversalValidationError> errors = [];
            if (!NodeExists(snapshot, query.SourceNodeStableKey!))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.SourceNodeNotFound, "The requested dependency path source node was not found in the selected snapshot."));
            }

            if (!NodeExists(snapshot, query.TargetNodeStableKey!))
            {
                errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.TargetNodeNotFound, "The requested dependency path target node was not found in the selected snapshot."));
            }

            return errors;
        }

        /// <summary>
        /// Normalizes and validates edge-kind filters.
        /// </summary>
        /// <param name="edgeKinds">The optional caller-supplied edge-kind filters.</param>
        /// <param name="errors">The validation error collection that receives unsupported edge-kind diagnostics.</param>
        /// <returns>The normalized edge-kind values to apply to traversal.</returns>
        private static string[] NormalizeEdgeKinds(IReadOnlyList<string> edgeKinds, List<GraphTraversalValidationError> errors)
        {
            // Empty edge-kind filters use the default dependency-like edge set so direct dependency endpoints are useful without extra parameters.
            string[] requested = edgeKinds
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(static value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (requested.Length == 0)
            {
                return s_defaultDependencyEdgeKinds;
            }

            List<string> normalized = [];
            foreach (string requestedKind in requested)
            {
                EdgeKind? match = EdgeKind.All.FirstOrDefault(kind => string.Equals(kind.Value, requestedKind, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    errors.Add(new GraphTraversalValidationError(GraphTraversalValidationCodes.EdgeKindUnsupported, $"Edge kind '{requestedKind}' is not supported by the controlled graph vocabulary."));
                }
                else
                {
                    normalized.Add(match.Value);
                }
            }

            return normalized.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Determines whether a traversal direction value is supported by public traversal endpoints.
        /// </summary>
        /// <param name="direction">The direction value to inspect.</param>
        /// <returns><see langword="true"/> when the direction is supported; otherwise, <see langword="false"/>.</returns>
        private static bool IsSupportedDirection(string direction)
        {
            // The endpoint intentionally supports a small set rather than accepting arbitrary traversal expressions.
            return string.Equals(direction, "Outgoing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "Incoming", StringComparison.OrdinalIgnoreCase)
                || string.Equals(direction, "Both", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts supported direction input to the canonical public direction name.
        /// </summary>
        /// <param name="direction">The supported direction value.</param>
        /// <returns>The canonical direction name used in response metadata.</returns>
        private static string NormalizeDirectionName(string direction)
        {
            // Canonical casing keeps JSON responses stable even when callers vary query-string casing.
            if (string.Equals(direction, "Incoming", StringComparison.OrdinalIgnoreCase))
            {
                return "Incoming";
            }

            return string.Equals(direction, "Both", StringComparison.OrdinalIgnoreCase) ? "Both" : "Outgoing";
        }

        /// <summary>
        /// Builds the traversal query context shared by API envelopes.
        /// </summary>
        /// <param name="selector">The caller-supplied traversal snapshot selector.</param>
        /// <param name="resolution">The successful snapshot resolution.</param>
        /// <returns>The traversal query context for response mapping.</returns>
        private static GraphTraversalQueryContext BuildContext(GraphTraversalSnapshotSelector selector, SnapshotResolution resolution)
        {
            // Context construction centralizes envelope metadata so every traversal endpoint reports scope consistently.
            ExtractedArchitectureSnapshot snapshot = resolution.Snapshot!;
            RepositoryModel? repository = snapshot.Repositories.FirstOrDefault(repository => StringComparer.Ordinal.Equals(repository.StableKey.Value, selector.RepositoryStableKey));
            SolutionModel? solution = selector.SolutionStableKey is null
                ? snapshot.Solutions.OrderBy(static candidate => candidate.StableKey.Value, StringComparer.Ordinal).FirstOrDefault()
                : snapshot.Solutions.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.StableKey.Value, selector.SolutionStableKey));
            ProjectScopeDto scope = new(selector.RepositoryStableKey!, repository?.Name, solution?.StableKey.Value, solution?.Name);
            ProjectSnapshotMetadataDto snapshotMetadata = new(
                snapshot.SnapshotHeader!.StableKey.Value,
                selector.SnapshotStableKey,
                selector.RequestsLatestSnapshot,
                snapshot.SnapshotHeader.CommitSha,
                snapshot.SnapshotHeader.StartedUtc,
                snapshot.SnapshotHeader.CompletedUtc,
                snapshot.SnapshotHeader.Status);
            GraphTraversalWarningDto[] warnings = snapshot.Warnings.Select(static warning => new GraphTraversalWarningDto("SnapshotWarning", warning)).ToArray();
            GraphTraversalUnknownDto[] unknowns = snapshot.Errors.Any()
                ? [new GraphTraversalUnknownDto("snapshotDiagnostics", "The selected snapshot contains extraction errors, so traversal data may be incomplete.")]
                : [];
            return new GraphTraversalQueryContext(scope, snapshotMetadata, warnings, unknowns);
        }

        /// <summary>
        /// Performs breadth-first graph exploration with fixed direction, depth, edge-kind, and result-count limits.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="startNodeStableKey">The stable key of the traversal start node.</param>
        /// <param name="direction">The normalized traversal direction.</param>
        /// <param name="maximumDepth">The maximum number of hops to explore.</param>
        /// <param name="edgeKinds">The normalized edge-kind filter values.</param>
        /// <param name="take">The maximum number of edge records returned by the response.</param>
        /// <param name="cancellationToken">The cancellation token that can stop graph exploration.</param>
        /// <returns>The bounded traversal work result.</returns>
        private static TraversalWorkResult Explore(ExtractedArchitectureSnapshot snapshot, string startNodeStableKey, string direction, int maximumDepth, IReadOnlyList<string> edgeKinds, int take, CancellationToken cancellationToken)
        {
            // Breadth-first traversal keeps memory bounded to visited nodes, frontier nodes, and matching edge rows rather than loading paths for the whole graph estate.
            HashSet<string> allowedEdgeKinds = new(edgeKinds, StringComparer.Ordinal);
            Queue<(string NodeStableKey, int Depth)> frontier = new();
            HashSet<string> visitedNodes = new(StringComparer.Ordinal) { startNodeStableKey };
            Dictionary<string, ArchitectureEdge> matchedEdges = new(StringComparer.Ordinal);
            bool truncated = false;
            frontier.Enqueue((startNodeStableKey, 0));

            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string currentNode, int currentDepth) = frontier.Dequeue();
                if (currentDepth >= maximumDepth)
                {
                    continue;
                }

                foreach (ArchitectureEdge edge in GetCandidateEdges(snapshot, currentNode, direction, allowedEdgeKinds))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    matchedEdges.TryAdd(edge.StableKey.Value, edge);
                    string nextNode = GetOppositeNode(edge, currentNode);
                    if (visitedNodes.Add(nextNode))
                    {
                        frontier.Enqueue((nextNode, currentDepth + 1));
                    }

                    if (matchedEdges.Count > take)
                    {
                        truncated = true;
                    }
                }
            }

            ArchitectureEdge[] orderedEdges = matchedEdges.Values
                .OrderBy(static edge => edge.StableKey.Value, StringComparer.Ordinal)
                .Take(take)
                .ToArray();
            GraphTraversalTruncationDto truncation = new(truncated, take, orderedEdges.Length, truncated ? "Traversal results exceeded the requested result limit." : null);
            return new TraversalWorkResult(orderedEdges, truncation);
        }

        /// <summary>
        /// Builds a public traversal response from bounded traversal work output.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="startNodeStableKey">The stable key of the traversal start node.</param>
        /// <param name="mode">The public traversal mode that produced the response.</param>
        /// <param name="direction">The normalized traversal direction.</param>
        /// <param name="depth">The applied maximum traversal depth.</param>
        /// <param name="edgeKinds">The normalized edge-kind filters.</param>
        /// <param name="work">The bounded traversal work output.</param>
        /// <returns>The public traversal response DTO.</returns>
        private static GraphTraversalResponseDto BuildTraversalResponse(ExtractedArchitectureSnapshot snapshot, string startNodeStableKey, string mode, string direction, int depth, IReadOnlyList<string> edgeKinds, TraversalWorkResult work)
        {
            // Node output is limited to the start node and edge endpoints needed to explain the returned edge set.
            HashSet<string> nodeKeys = new(StringComparer.Ordinal) { startNodeStableKey };
            foreach (ArchitectureEdge edge in work.Edges)
            {
                nodeKeys.Add(edge.SourceNodeStableKey.Value);
                nodeKeys.Add(edge.TargetNodeStableKey.Value);
            }

            GraphNodeDto[] nodes = nodeKeys
                .Select(key => snapshot.Nodes.FirstOrDefault(node => StringComparer.Ordinal.Equals(node.StableKey.Value, key)))
                .Where(static node => node is not null)
                .Select(node => ToGraphNode(node!))
                .OrderBy(static node => node.StableKey, StringComparer.Ordinal)
                .ToArray();
            GraphEdgeDto[] edges = work.Edges.Select(ToGraphEdge).ToArray();
            return new GraphTraversalResponseDto(startNodeStableKey, mode, direction, depth, edgeKinds.ToArray(), nodes, edges, work.Truncation);
        }

        /// <summary>
        /// Builds a public dependency-path response using bounded breadth-first search.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target node.</param>
        /// <param name="maximumDepth">The maximum number of hops to search.</param>
        /// <param name="edgeKinds">The normalized edge-kind filters.</param>
        /// <param name="cancellationToken">The cancellation token that can stop path search.</param>
        /// <returns>The public path response DTO.</returns>
        private static DependencyPathResponseDto BuildDependencyPathResponse(ExtractedArchitectureSnapshot snapshot, string sourceNodeStableKey, string targetNodeStableKey, int maximumDepth, IReadOnlyList<string> edgeKinds, CancellationToken cancellationToken)
        {
            // Path search uses outgoing edges because dependency paths explain how one node depends on another in edge direction order.
            if (snapshot.Edges.Count == 0)
            {
                GraphTraversalTruncationDto unavailableTruncation = new(false, GraphTraversalLimits.MaximumResultLimit, 0, null);
                return new DependencyPathResponseDto(sourceNodeStableKey, targetNodeStableKey, false, true, "The selected snapshot contains no persisted graph edges, so dependency path data is unavailable.", maximumDepth, edgeKinds.ToArray(), [], [], unavailableTruncation);
            }

            HashSet<string> allowedEdgeKinds = new(edgeKinds, StringComparer.Ordinal);
            Queue<PathState> frontier = new();
            HashSet<string> visitedNodes = new(StringComparer.Ordinal) { sourceNodeStableKey };
            frontier.Enqueue(new PathState(sourceNodeStableKey, []));

            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PathState state = frontier.Dequeue();
                if (state.Edges.Count >= maximumDepth)
                {
                    continue;
                }

                foreach (ArchitectureEdge edge in snapshot.Edges
                    .Where(edge => allowedEdgeKinds.Contains(edge.EdgeKind.Value) && StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, state.NodeStableKey))
                    .OrderBy(static edge => edge.StableKey.Value, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ArchitectureEdge[] nextEdges = [.. state.Edges, edge];
                    if (StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, targetNodeStableKey))
                    {
                        return BuildFoundPathResponse(snapshot, sourceNodeStableKey, targetNodeStableKey, maximumDepth, edgeKinds, nextEdges);
                    }

                    if (visitedNodes.Add(edge.TargetNodeStableKey.Value))
                    {
                        frontier.Enqueue(new PathState(edge.TargetNodeStableKey.Value, nextEdges));
                    }
                }
            }

            GraphTraversalTruncationDto truncation = new(false, GraphTraversalLimits.MaximumResultLimit, 0, null);
            return new DependencyPathResponseDto(sourceNodeStableKey, targetNodeStableKey, false, false, "No dependency path was found within the requested depth and edge-kind bounds.", maximumDepth, edgeKinds.ToArray(), [], [], truncation);
        }

        /// <summary>
        /// Builds a successful dependency-path response from an ordered edge path.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target node.</param>
        /// <param name="maximumDepth">The applied maximum path-search depth.</param>
        /// <param name="edgeKinds">The normalized edge-kind filters.</param>
        /// <param name="pathEdges">The ordered edge path that connects source to target.</param>
        /// <returns>The public path response DTO for a found path.</returns>
        private static DependencyPathResponseDto BuildFoundPathResponse(ExtractedArchitectureSnapshot snapshot, string sourceNodeStableKey, string targetNodeStableKey, int maximumDepth, IReadOnlyList<string> edgeKinds, IReadOnlyList<ArchitectureEdge> pathEdges)
        {
            // Node order follows the path sequence: source node first, then each edge target in hop order.
            List<string> nodePath = [sourceNodeStableKey];
            nodePath.AddRange(pathEdges.Select(static edge => edge.TargetNodeStableKey.Value));
            GraphNodeDto[] nodes = nodePath
                .Select(key => snapshot.Nodes.First(node => StringComparer.Ordinal.Equals(node.StableKey.Value, key)))
                .Select(ToGraphNode)
                .ToArray();
            GraphEdgeDto[] edges = pathEdges.Select(ToGraphEdge).ToArray();
            GraphTraversalTruncationDto truncation = new(false, GraphTraversalLimits.MaximumResultLimit, edges.Length, null);
            return new DependencyPathResponseDto(sourceNodeStableKey, targetNodeStableKey, true, false, null, maximumDepth, edgeKinds.ToArray(), nodes, edges, truncation);
        }

        /// <summary>
        /// Gets candidate edges adjacent to a current traversal node for the requested direction.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="currentNodeStableKey">The current traversal node stable key.</param>
        /// <param name="direction">The normalized traversal direction.</param>
        /// <param name="allowedEdgeKinds">The allowed edge-kind values.</param>
        /// <returns>The stable ordered candidate edges.</returns>
        private static IEnumerable<ArchitectureEdge> GetCandidateEdges(ExtractedArchitectureSnapshot snapshot, string currentNodeStableKey, string direction, HashSet<string> allowedEdgeKinds)
        {
            // Direction handling is explicit so dependents, dependencies, and neighbourhood endpoints share one bounded traversal algorithm.
            return snapshot.Edges
                .Where(edge => allowedEdgeKinds.Contains(edge.EdgeKind.Value))
                .Where(edge => DirectionMatches(edge, currentNodeStableKey, direction))
                .OrderBy(static edge => edge.StableKey.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Determines whether an edge is adjacent to a node under the requested direction.
        /// </summary>
        /// <param name="edge">The candidate graph edge.</param>
        /// <param name="currentNodeStableKey">The current traversal node stable key.</param>
        /// <param name="direction">The normalized traversal direction.</param>
        /// <returns><see langword="true"/> when the edge matches the direction; otherwise, <see langword="false"/>.</returns>
        private static bool DirectionMatches(ArchitectureEdge edge, string currentNodeStableKey, string direction)
        {
            // Incoming and outgoing checks remain stable-key based so persistence-local identifiers never participate in traversal logic.
            bool outgoing = StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, currentNodeStableKey);
            bool incoming = StringComparer.Ordinal.Equals(edge.TargetNodeStableKey.Value, currentNodeStableKey);
            return direction switch
            {
                "Incoming" => incoming,
                "Both" => outgoing || incoming,
                _ => outgoing
            };
        }

        /// <summary>
        /// Gets the opposite endpoint stable key for an edge adjacent to the current node.
        /// </summary>
        /// <param name="edge">The adjacent graph edge.</param>
        /// <param name="currentNodeStableKey">The current traversal node stable key.</param>
        /// <returns>The stable key of the node at the other end of the edge.</returns>
        private static string GetOppositeNode(ArchitectureEdge edge, string currentNodeStableKey)
        {
            // In both-direction traversal, either side can be the current node, so the opposite endpoint advances the frontier.
            return StringComparer.Ordinal.Equals(edge.SourceNodeStableKey.Value, currentNodeStableKey)
                ? edge.TargetNodeStableKey.Value
                : edge.SourceNodeStableKey.Value;
        }

        /// <summary>
        /// Determines whether a stable node identity exists in the selected snapshot.
        /// </summary>
        /// <param name="snapshot">The selected architecture snapshot.</param>
        /// <param name="nodeStableKey">The stable node key to find.</param>
        /// <returns><see langword="true"/> when the node exists; otherwise, <see langword="false"/>.</returns>
        private static bool NodeExists(ExtractedArchitectureSnapshot snapshot, string nodeStableKey)
        {
            // Stable-key lookup is the only supported public identity lookup for traversal endpoints.
            return snapshot.Nodes.Any(node => StringComparer.Ordinal.Equals(node.StableKey.Value, nodeStableKey));
        }

        /// <summary>
        /// Maps an architecture node into a public traversal node DTO.
        /// </summary>
        /// <param name="node">The architecture node to map.</param>
        /// <returns>The public traversal node DTO.</returns>
        private static GraphNodeDto ToGraphNode(ArchitectureNode node)
        {
            // Node DTOs expose stable identity, vocabulary, confidence, evidence references, and unknown state without metadata maps or database IDs.
            string[] evidenceStableKeys = node.PrimaryEvidenceStableKey is StableKey primaryEvidenceStableKey ? [primaryEvidenceStableKey.Value] : [];
            return new GraphNodeDto(
                node.StableKey.Value,
                node.NodeKind.Value,
                node.DisplayName,
                node.ProjectStableKey?.Value,
                evidenceStableKeys,
                node.Confidence.Value,
                node.UnknownState.HasUnknownData,
                node.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Maps an architecture edge into a public traversal edge DTO.
        /// </summary>
        /// <param name="edge">The architecture edge to map.</param>
        /// <returns>The public traversal edge DTO.</returns>
        private static GraphEdgeDto ToGraphEdge(ArchitectureEdge edge)
        {
            // Edge DTOs expose stable endpoints and evidence references without raw graph-store details.
            string[] evidenceStableKeys = edge.PrimaryEvidenceStableKey is StableKey primaryEvidenceStableKey ? [primaryEvidenceStableKey.Value] : [];
            return new GraphEdgeDto(
                edge.StableKey.Value,
                edge.EdgeKind.Value,
                edge.SourceNodeStableKey.Value,
                edge.TargetNodeStableKey.Value,
                edge.IsDirect,
                evidenceStableKeys,
                edge.Confidence.Value,
                edge.UnknownState.HasUnknownData,
                edge.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Adds truncation warnings and unknowns to a traversal query context when limits apply.
        /// </summary>
        /// <param name="context">The original traversal context.</param>
        /// <param name="truncation">The traversal truncation metadata.</param>
        /// <returns>The context augmented with truncation diagnostics when needed.</returns>
        private static GraphTraversalQueryContext MergeTruncationContext(GraphTraversalQueryContext context, GraphTraversalTruncationDto truncation)
        {
            // Truncation is a safe warning and unknown because callers know the returned subgraph is incomplete by design.
            if (!truncation.Truncated)
            {
                return context;
            }

            GraphTraversalWarningDto warning = new("TraversalTruncated", truncation.Reason ?? "Traversal results were truncated by the configured limit.");
            GraphTraversalUnknownDto unknown = new("traversalResults", truncation.Reason ?? "Traversal results were truncated by the configured limit.");
            return new GraphTraversalQueryContext(context.Scope, context.Snapshot, context.Warnings.Concat([warning]).ToArray(), context.Unknowns.Concat([unknown]).ToArray());
        }

        /// <summary>
        /// Adds an unknown value to a traversal query context.
        /// </summary>
        /// <param name="context">The original traversal context.</param>
        /// <param name="unknown">The unknown value to add.</param>
        /// <returns>The context augmented with the unknown value.</returns>
        private static GraphTraversalQueryContext MergeUnknownContext(GraphTraversalQueryContext context, GraphTraversalUnknownDto unknown)
        {
            // Unavailable-data path results use envelope unknowns so clients can react without treating the response as an exception.
            return new GraphTraversalQueryContext(context.Scope, context.Snapshot, context.Warnings, context.Unknowns.Concat([unknown]).ToArray());
        }

        /// <summary>
        /// Represents snapshot resolution state for traversal queries.
        /// </summary>
        private sealed class SnapshotResolution
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="SnapshotResolution"/> class.
            /// </summary>
            /// <param name="snapshot">The resolved snapshot when resolution succeeds.</param>
            /// <param name="validationErrors">The validation errors that explain failed resolution.</param>
            private SnapshotResolution(ExtractedArchitectureSnapshot? snapshot, IReadOnlyList<GraphTraversalValidationError> validationErrors)
            {
                // Resolution stores either a selected snapshot or deterministic validation errors, never both.
                Snapshot = snapshot;
                ValidationErrors = validationErrors;
            }

            /// <summary>
            /// Gets a value indicating whether snapshot resolution succeeded.
            /// </summary>
            public bool Succeeded => ValidationErrors.Count == 0;

            /// <summary>
            /// Gets the resolved snapshot when resolution succeeds.
            /// </summary>
            public ExtractedArchitectureSnapshot? Snapshot { get; }

            /// <summary>
            /// Gets validation errors that explain failed resolution.
            /// </summary>
            public IReadOnlyList<GraphTraversalValidationError> ValidationErrors { get; }

            /// <summary>
            /// Creates a successful snapshot resolution.
            /// </summary>
            /// <param name="snapshot">The resolved snapshot.</param>
            /// <param name="scopedSnapshots">The scoped snapshot set, retained for signature parity with other WP014 resolvers.</param>
            /// <returns>A successful snapshot resolution.</returns>
            public static SnapshotResolution Success(ExtractedArchitectureSnapshot snapshot, IReadOnlyList<ExtractedArchitectureSnapshot> scopedSnapshots)
            {
                // The scoped snapshot parameter documents that latest resolution considered a bounded set even though traversal only needs one snapshot.
                _ = scopedSnapshots;
                return new SnapshotResolution(snapshot, []);
            }

            /// <summary>
            /// Creates a failed snapshot resolution.
            /// </summary>
            /// <param name="validationErrors">The validation errors that explain failed resolution.</param>
            /// <returns>A failed snapshot resolution.</returns>
            public static SnapshotResolution Failed(IReadOnlyList<GraphTraversalValidationError> validationErrors)
            {
                // Failed resolution cannot safely run traversal because the selected graph scope is unknown.
                return new SnapshotResolution(null, validationErrors);
            }
        }

        /// <summary>
        /// Represents normalized traversal options after validation.
        /// </summary>
        private sealed class TraversalOptionsResult
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="TraversalOptionsResult"/> class.
            /// </summary>
            /// <param name="direction">The normalized traversal direction when validation succeeds.</param>
            /// <param name="depth">The normalized traversal depth when validation succeeds.</param>
            /// <param name="take">The normalized result limit when validation succeeds.</param>
            /// <param name="edgeKinds">The normalized edge-kind filters when validation succeeds.</param>
            /// <param name="validationErrors">The validation errors that explain failed option normalization.</param>
            private TraversalOptionsResult(string? direction, int depth, int take, IReadOnlyList<string> edgeKinds, IReadOnlyList<GraphTraversalValidationError> validationErrors)
            {
                // Option normalization stores either safe bounded values or deterministic validation errors.
                Direction = direction;
                Depth = depth;
                Take = take;
                EdgeKinds = edgeKinds;
                ValidationErrors = validationErrors;
            }

            /// <summary>
            /// Gets a value indicating whether option normalization succeeded.
            /// </summary>
            public bool Succeeded => ValidationErrors.Count == 0;

            /// <summary>
            /// Gets the normalized traversal direction when validation succeeds.
            /// </summary>
            public string? Direction { get; }

            /// <summary>
            /// Gets the normalized traversal depth when validation succeeds.
            /// </summary>
            public int Depth { get; }

            /// <summary>
            /// Gets the normalized result limit when validation succeeds.
            /// </summary>
            public int Take { get; }

            /// <summary>
            /// Gets the normalized edge-kind filters when validation succeeds.
            /// </summary>
            public IReadOnlyList<string> EdgeKinds { get; }

            /// <summary>
            /// Gets validation errors that explain failed option normalization.
            /// </summary>
            public IReadOnlyList<GraphTraversalValidationError> ValidationErrors { get; }

            /// <summary>
            /// Creates a successful traversal option result.
            /// </summary>
            /// <param name="direction">The normalized traversal direction.</param>
            /// <param name="depth">The normalized traversal depth.</param>
            /// <param name="take">The normalized result limit.</param>
            /// <param name="edgeKinds">The normalized edge-kind filters.</param>
            /// <returns>A successful traversal option result.</returns>
            public static TraversalOptionsResult Success(string direction, int depth, int take, IReadOnlyList<string> edgeKinds)
            {
                // Safe options can be used directly by traversal without additional bounds checks.
                return new TraversalOptionsResult(direction, depth, take, edgeKinds, []);
            }

            /// <summary>
            /// Creates a failed traversal option result.
            /// </summary>
            /// <param name="validationErrors">The validation errors that explain failed option normalization.</param>
            /// <returns>A failed traversal option result.</returns>
            public static TraversalOptionsResult Failed(IReadOnlyList<GraphTraversalValidationError> validationErrors)
            {
                // Failed option validation prevents graph exploration from running with unbounded or unsupported settings.
                return new TraversalOptionsResult(null, 0, 0, [], validationErrors);
            }
        }

        /// <summary>
        /// Represents bounded traversal work output before API DTO shaping.
        /// </summary>
        /// <param name="Edges">The ordered bounded edge set returned by traversal.</param>
        /// <param name="Truncation">The truncation metadata produced while limiting the edge set.</param>
        private sealed record TraversalWorkResult(IReadOnlyList<ArchitectureEdge> Edges, GraphTraversalTruncationDto Truncation);

        /// <summary>
        /// Represents one path-search frontier state.
        /// </summary>
        /// <param name="NodeStableKey">The current node stable key at the end of the path state.</param>
        /// <param name="Edges">The ordered edge path used to reach the current node.</param>
        private sealed record PathState(string NodeStableKey, IReadOnlyList<ArchitectureEdge> Edges);
    }
}
