namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Defines the application-layer port for initializing the architecture graph store.
    /// </summary>
    /// <remarks>
    /// The port belongs in the application layer because hosts and orchestration code need a stable operation they can call without
    /// depending on Neo4j driver types. Infrastructure adapters implement the port for a specific persistence technology.
    /// </remarks>
    public interface IArchitectureGraphInitializer
    {
        /// <summary>
        /// Ensures the configured graph store has the constraints and indexes required by Archon persistence.
        /// </summary>
        /// <param name="cancellationToken">A token that cancels initialization before or between asynchronous schema operations.</param>
        /// <returns>A result describing whether initialization succeeded, how many statements ran, and any safe diagnostics.</returns>
        Task<GraphInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
    }
}
