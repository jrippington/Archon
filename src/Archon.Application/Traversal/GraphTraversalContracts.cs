using Archon.Application.Projects;

namespace Archon.Application.Traversal
{
    /// <summary>
    /// Defines the stable defaults and safety limits used by bounded graph traversal queries.
    /// </summary>
    public static class GraphTraversalLimits
    {
        /// <summary>
        /// Defines the default depth for graph-neighbourhood queries when callers do not supply an explicit depth.
        /// </summary>
        public const int DefaultNeighbourhoodDepth = 1;

        /// <summary>
        /// Defines the default depth for transitive dependency and dependent traversal queries.
        /// </summary>
        public const int DefaultTransitiveDepth = 3;

        /// <summary>
        /// Defines the maximum traversal depth accepted by public traversal endpoints.
        /// </summary>
        public const int MaximumDepth = 6;

        /// <summary>
        /// Defines the default number of graph edges returned when callers do not supply an explicit result limit.
        /// </summary>
        public const int DefaultResultLimit = 100;

        /// <summary>
        /// Defines the maximum graph edge count that a single traversal endpoint may return.
        /// </summary>
        public const int MaximumResultLimit = 500;
    }

    /// <summary>
    /// Defines stable validation codes for controlled graph traversal queries.
    /// </summary>
    public static class GraphTraversalValidationCodes
    {
        /// <summary>
        /// Indicates that a repository stable key was not supplied for a traversal query.
        /// </summary>
        public const string RepositoryStableKeyRequired = nameof(RepositoryStableKeyRequired);

        /// <summary>
        /// Indicates that the supplied snapshot selector was neither latest/current nor a snapshot stable key.
        /// </summary>
        public const string SnapshotSelectorInvalid = nameof(SnapshotSelectorInvalid);

        /// <summary>
        /// Indicates that the requested repository scope does not exist in persisted snapshots.
        /// </summary>
        public const string RepositoryNotFound = nameof(RepositoryNotFound);

        /// <summary>
        /// Indicates that the requested solution scope does not exist within the repository scope.
        /// </summary>
        public const string SolutionNotFound = nameof(SolutionNotFound);

        /// <summary>
        /// Indicates that the requested snapshot scope does not exist.
        /// </summary>
        public const string SnapshotNotFound = nameof(SnapshotNotFound);

        /// <summary>
        /// Indicates that a traversal start node stable key was not supplied.
        /// </summary>
        public const string NodeStableKeyRequired = nameof(NodeStableKeyRequired);

        /// <summary>
        /// Indicates that a dependency path source node stable key was not supplied.
        /// </summary>
        public const string SourceNodeStableKeyRequired = nameof(SourceNodeStableKeyRequired);

        /// <summary>
        /// Indicates that a dependency path target node stable key was not supplied.
        /// </summary>
        public const string TargetNodeStableKeyRequired = nameof(TargetNodeStableKeyRequired);

        /// <summary>
        /// Indicates that the requested start node does not exist in the selected snapshot.
        /// </summary>
        public const string NodeNotFound = nameof(NodeNotFound);

        /// <summary>
        /// Indicates that the requested dependency path source node does not exist in the selected snapshot.
        /// </summary>
        public const string SourceNodeNotFound = nameof(SourceNodeNotFound);

        /// <summary>
        /// Indicates that the requested dependency path target node does not exist in the selected snapshot.
        /// </summary>
        public const string TargetNodeNotFound = nameof(TargetNodeNotFound);

        /// <summary>
        /// Indicates that the supplied traversal direction is not supported.
        /// </summary>
        public const string DirectionUnsupported = nameof(DirectionUnsupported);

        /// <summary>
        /// Indicates that at least one supplied edge kind is not part of the controlled graph vocabulary.
        /// </summary>
        public const string EdgeKindUnsupported = nameof(EdgeKindUnsupported);

        /// <summary>
        /// Indicates that the requested traversal depth is outside the supported bounds.
        /// </summary>
        public const string DepthInvalid = nameof(DepthInvalid);

        /// <summary>
        /// Indicates that the requested traversal result limit is outside the supported bounds.
        /// </summary>
        public const string TakeInvalid = nameof(TakeInvalid);
    }

    /// <summary>
    /// Represents one deterministic validation problem produced by a traversal query.
    /// </summary>
    /// <param name="Code">The stable machine-readable validation code.</param>
    /// <param name="Message">The safe developer-facing validation message.</param>
    public sealed record GraphTraversalValidationError(string Code, string Message);

    /// <summary>
    /// Represents one safe warning emitted by a traversal query when response data is partial or bounded.
    /// </summary>
    /// <param name="Code">The stable machine-readable warning code.</param>
    /// <param name="Message">The safe developer-facing warning message.</param>
    public sealed record GraphTraversalWarningDto(string Code, string Message);

    /// <summary>
    /// Represents one explicit unknown field emitted by a traversal query when data availability cannot be proven.
    /// </summary>
    /// <param name="Field">The response field or concept whose value is unknown.</param>
    /// <param name="Reason">The safe reason that explains why the value is unknown.</param>
    public sealed record GraphTraversalUnknownDto(string Field, string Reason);

    /// <summary>
    /// Describes the request scope and snapshot selector shared by traversal endpoints.
    /// </summary>
    public sealed class GraphTraversalSnapshotSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphTraversalSnapshotSelector"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="snapshotStableKey">The exact snapshot stable key or latest/current selector supplied by the caller.</param>
        public GraphTraversalSnapshotSelector(string? repositoryStableKey, string? solutionStableKey, string? snapshotStableKey)
        {
            // Traversal selectors use the same normalization pattern as project queries so all WP014 scope handling stays consistent.
            RepositoryStableKey = NormalizeOptional(repositoryStableKey);
            SolutionStableKey = NormalizeOptional(solutionStableKey);
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? "latest" : snapshotStableKey.Trim();
        }

        /// <summary>
        /// Gets the repository stable key that bounds latest/current snapshot resolution.
        /// </summary>
        public string? RepositoryStableKey { get; }

        /// <summary>
        /// Gets the optional solution stable key that narrows repository scope.
        /// </summary>
        public string? SolutionStableKey { get; }

        /// <summary>
        /// Gets the exact snapshot stable key or latest/current selector supplied by the caller.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets a value indicating whether the selector requests deterministic latest/current snapshot resolution.
        /// </summary>
        public bool RequestsLatestSnapshot => string.Equals(SnapshotStableKey, "latest", StringComparison.OrdinalIgnoreCase) || string.Equals(SnapshotStableKey, "current", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Normalizes optional selector text into trimmed values or null.
        /// </summary>
        /// <param name="value">The optional selector value supplied by the caller.</param>
        /// <returns>The trimmed value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Blank selector fields should behave like omitted fields rather than introducing invisible whitespace identities.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Represents a controlled graph traversal request for dependency, dependent, and neighbourhood endpoint families.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the traversal.</param>
    /// <param name="NodeStableKey">The stable key of the start node for traversal.</param>
    /// <param name="Direction">The requested traversal direction: Outgoing, Incoming, or Both.</param>
    /// <param name="Depth">The maximum number of graph hops to traverse.</param>
    /// <param name="EdgeKinds">The optional controlled edge-kind filter values.</param>
    /// <param name="Take">The maximum number of edges returned by the response.</param>
    /// <param name="Mode">The public traversal mode used for response metadata and diagnostics.</param>
    public sealed record GraphTraversalQuery(GraphTraversalSnapshotSelector Selector, string? NodeStableKey, string? Direction, int Depth, IReadOnlyList<string> EdgeKinds, int Take, string Mode);

    /// <summary>
    /// Represents a controlled dependency-path request between two stable graph node identities.
    /// </summary>
    /// <param name="Selector">The repository, solution, and snapshot selector that scopes the path search.</param>
    /// <param name="SourceNodeStableKey">The stable key of the source node where path search starts.</param>
    /// <param name="TargetNodeStableKey">The stable key of the target node where path search ends.</param>
    /// <param name="Depth">The maximum number of graph hops to search.</param>
    /// <param name="EdgeKinds">The optional controlled edge-kind filter values.</param>
    public sealed record DependencyPathQuery(GraphTraversalSnapshotSelector Selector, string? SourceNodeStableKey, string? TargetNodeStableKey, int Depth, IReadOnlyList<string> EdgeKinds);

    /// <summary>
    /// Represents one stable graph node returned by traversal endpoints.
    /// </summary>
    /// <param name="StableKey">The durable public node identity.</param>
    /// <param name="Kind">The controlled graph node kind.</param>
    /// <param name="DisplayName">The developer-facing display name for the node.</param>
    /// <param name="ProjectStableKey">The owning project stable key when the node is project-owned.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the node.</param>
    /// <param name="Confidence">The normalized confidence value assigned to the node.</param>
    /// <param name="HasUnknownData">A value indicating whether the node has explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown node data.</param>
    public sealed record GraphNodeDto(string StableKey, string Kind, string DisplayName, string? ProjectStableKey, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents one stable graph edge returned by traversal endpoints.
    /// </summary>
    /// <param name="StableKey">The durable public edge identity.</param>
    /// <param name="Kind">The controlled graph edge kind.</param>
    /// <param name="SourceNodeStableKey">The stable key of the source node.</param>
    /// <param name="TargetNodeStableKey">The stable key of the target node.</param>
    /// <param name="IsDirect">A value indicating whether the relationship was directly observed.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the edge.</param>
    /// <param name="Confidence">The normalized confidence value assigned to the edge.</param>
    /// <param name="HasUnknownData">A value indicating whether the edge has explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown edge data.</param>
    public sealed record GraphEdgeDto(string StableKey, string Kind, string SourceNodeStableKey, string TargetNodeStableKey, bool IsDirect, IReadOnlyList<string> EvidenceStableKeys, decimal Confidence, bool HasUnknownData, string? UnknownReason);

    /// <summary>
    /// Represents response-size limiting metadata calculated by the application traversal service.
    /// </summary>
    /// <param name="Truncated">A value indicating whether matching traversal data exceeded the requested result limit.</param>
    /// <param name="Limit">The applied result limit.</param>
    /// <param name="ReturnedCount">The number of edge records returned after limiting.</param>
    /// <param name="Reason">The safe explanation for truncation when truncation occurred.</param>
    public sealed record GraphTraversalTruncationDto(bool Truncated, int Limit, int ReturnedCount, string? Reason);

    /// <summary>
    /// Represents a bounded graph traversal response containing stable nodes, stable edges, and traversal metadata.
    /// </summary>
    /// <param name="StartNodeStableKey">The stable key of the traversal start node.</param>
    /// <param name="Mode">The public traversal mode that produced the response.</param>
    /// <param name="Direction">The normalized traversal direction.</param>
    /// <param name="Depth">The applied maximum traversal depth.</param>
    /// <param name="EdgeKinds">The normalized edge-kind filter values applied to the traversal.</param>
    /// <param name="Nodes">The stable graph nodes needed to explain returned edges.</param>
    /// <param name="Edges">The stable graph edges returned by the traversal.</param>
    /// <param name="Truncation">The application-level truncation metadata for the traversal.</param>
    public sealed record GraphTraversalResponseDto(string StartNodeStableKey, string Mode, string Direction, int Depth, IReadOnlyList<string> EdgeKinds, IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges, GraphTraversalTruncationDto Truncation);

    /// <summary>
    /// Represents a dependency path response that can distinguish path found, no-path, and unavailable-data states.
    /// </summary>
    /// <param name="SourceNodeStableKey">The stable key of the source node.</param>
    /// <param name="TargetNodeStableKey">The stable key of the target node.</param>
    /// <param name="PathFound">A value indicating whether a path was found.</param>
    /// <param name="Unavailable">A value indicating whether path data could not be determined from available persisted graph support.</param>
    /// <param name="Reason">The safe reason for no-path or unavailable-data results.</param>
    /// <param name="Depth">The applied maximum path-search depth.</param>
    /// <param name="EdgeKinds">The normalized edge-kind filter values applied to path search.</param>
    /// <param name="Nodes">The stable graph nodes in path order when a path is found.</param>
    /// <param name="Edges">The stable graph edges in path order when a path is found.</param>
    /// <param name="Truncation">The application-level response-size metadata for the path response.</param>
    public sealed record DependencyPathResponseDto(string SourceNodeStableKey, string TargetNodeStableKey, bool PathFound, bool Unavailable, string? Reason, int Depth, IReadOnlyList<string> EdgeKinds, IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges, GraphTraversalTruncationDto Truncation);

    /// <summary>
    /// Represents the response context shared by traversal envelopes.
    /// </summary>
    /// <param name="Scope">The resolved repository and optional solution scope.</param>
    /// <param name="Snapshot">The resolved snapshot metadata.</param>
    /// <param name="Warnings">The safe warnings emitted while building traversal output.</param>
    /// <param name="Unknowns">The explicit unknown fields emitted while building traversal output.</param>
    public sealed record GraphTraversalQueryContext(ProjectScopeDto Scope, ProjectSnapshotMetadataDto Snapshot, IReadOnlyList<GraphTraversalWarningDto> Warnings, IReadOnlyList<GraphTraversalUnknownDto> Unknowns);

    /// <summary>
    /// Represents the application result for a graph traversal request.
    /// </summary>
    public sealed class GraphTraversalResult
    {
        /// <summary>
        /// Initializes a successful graph traversal result.
        /// </summary>
        /// <param name="response">The bounded traversal response payload.</param>
        /// <param name="context">The traversal envelope context.</param>
        public GraphTraversalResult(GraphTraversalResponseDto response, GraphTraversalQueryContext context)
        {
            // Successful traversal results must include both data and envelope context so the API layer can serialize one complete response.
            Response = response ?? throw new ArgumentNullException(nameof(response));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed graph traversal result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why traversal did not run.</param>
        public GraphTraversalResult(IEnumerable<GraphTraversalValidationError> validationErrors)
        {
            // Failed traversal results carry deterministic validation errors and no response payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether traversal succeeded and produced a response payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the bounded traversal response payload when traversal succeeds.
        /// </summary>
        public GraphTraversalResponseDto? Response { get; }

        /// <summary>
        /// Gets the traversal envelope context when traversal succeeds.
        /// </summary>
        public GraphTraversalQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why traversal did not run.
        /// </summary>
        public IReadOnlyList<GraphTraversalValidationError> ValidationErrors { get; }
    }

    /// <summary>
    /// Represents the application result for a dependency-path query.
    /// </summary>
    public sealed class DependencyPathResult
    {
        /// <summary>
        /// Initializes a successful dependency-path result.
        /// </summary>
        /// <param name="response">The path response payload.</param>
        /// <param name="context">The traversal envelope context.</param>
        public DependencyPathResult(DependencyPathResponseDto response, GraphTraversalQueryContext context)
        {
            // Successful path results include no-path and unavailable-data payloads as normal data, not validation failures.
            Response = response ?? throw new ArgumentNullException(nameof(response));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ValidationErrors = [];
        }

        /// <summary>
        /// Initializes a failed dependency-path result with validation errors.
        /// </summary>
        /// <param name="validationErrors">The validation errors that explain why path search did not run.</param>
        public DependencyPathResult(IEnumerable<GraphTraversalValidationError> validationErrors)
        {
            // Failed path results carry deterministic validation errors and no response payload.
            ValidationErrors = validationErrors?.ToArray() ?? throw new ArgumentNullException(nameof(validationErrors));
        }

        /// <summary>
        /// Gets a value indicating whether path query validation succeeded and produced a response payload.
        /// </summary>
        public bool Succeeded => ValidationErrors.Count == 0;

        /// <summary>
        /// Gets the dependency-path response payload when validation succeeds.
        /// </summary>
        public DependencyPathResponseDto? Response { get; }

        /// <summary>
        /// Gets the traversal envelope context when validation succeeds.
        /// </summary>
        public GraphTraversalQueryContext? Context { get; }

        /// <summary>
        /// Gets the validation errors that explain why path search did not run.
        /// </summary>
        public IReadOnlyList<GraphTraversalValidationError> ValidationErrors { get; }
    }
}
