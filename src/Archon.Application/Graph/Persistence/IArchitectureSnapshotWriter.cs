using Archon.Application.Extraction.Contracts;

namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Defines the application-layer port for persisting assembled architecture snapshots.
    /// </summary>
    /// <remarks>
    /// The port deliberately accepts the application-owned <see cref="ExtractedArchitectureSnapshot"/> contract and returns
    /// application-owned result details. Infrastructure implementations, such as Neo4j, must translate those contracts without exposing
    /// database driver types to the application layer.
    /// </remarks>
    public interface IArchitectureSnapshotWriter
    {
        /// <summary>
        /// Persists one assembled architecture snapshot into the configured graph store.
        /// </summary>
        /// <param name="snapshot">The assembled architecture snapshot to persist.</param>
        /// <param name="cancellationToken">A token that cancels persistence before or during asynchronous store operations.</param>
        /// <returns>A result describing success, persisted counts, warnings, and safe errors.</returns>
        Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default);
    }
}
