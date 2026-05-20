namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Defines the application-layer port for explicitly destructive architecture graph recreation.
    /// </summary>
    /// <remarks>
    /// The port intentionally uses application-owned request and result types so callers can request a development or test graph reset
    /// without depending on Neo4j driver types. Implementations must treat this operation as destructive and must not expose it through
    /// ordinary startup, snapshot persistence, or production API behavior.
    /// </remarks>
    public interface IArchitectureGraphRecreator
    {
        /// <summary>
        /// Clears Archon-owned graph records and recreates required persistence schema when the request explicitly authorizes destruction.
        /// </summary>
        /// <param name="request">The explicit recreation request containing the destructive confirmation token.</param>
        /// <param name="cancellationToken">A token that cancels recreation before or between asynchronous graph operations.</param>
        /// <returns>A result describing whether recreation ran, how many records were deleted, and any safe diagnostics.</returns>
        Task<GraphRecreationResult> RecreateGraphAsync(GraphRecreationRequest request, CancellationToken cancellationToken = default);
    }
}
