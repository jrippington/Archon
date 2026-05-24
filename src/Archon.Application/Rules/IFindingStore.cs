using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Persists and retrieves WP012 findings, history, and suppression data without exposing infrastructure implementation details.
    /// </summary>
    public interface IFindingStore
    {
        /// <summary>
        /// Upserts snapshot-owned findings and their link information by snapshot stable key plus finding stable key.
        /// </summary>
        /// <param name="findings">The findings to persist or update.</param>
        /// <param name="cancellationToken">The cancellation token that can stop persistence before adapter work starts.</param>
        /// <returns>A result describing the finding upsert outcome and credential-safe diagnostics.</returns>
        Task<FindingUpsertResult> UpsertFindingsAsync(IEnumerable<FindingRecord> findings, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves persisted findings for one snapshot in deterministic stable-key order.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key whose findings should be retrieved.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>The persisted findings for the requested snapshot.</returns>
        Task<IReadOnlyList<FindingRecord>> GetFindingsBySnapshotAsync(string snapshotStableKey, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves a persisted finding by its snapshot scope and stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The finding stable key to retrieve.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>The matching finding, or <see langword="null"/> when no finding exists.</returns>
        Task<FindingRecord?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves cross-snapshot history seeds for the requested finding history keys.
        /// </summary>
        /// <param name="historyKeys">The deterministic finding history keys to resolve.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>The history seeds known to the store.</returns>
        Task<IReadOnlyList<FindingHistorySeed>> GetHistoryAsync(IEnumerable<string> historyKeys, CancellationToken cancellationToken);

        /// <summary>
        /// Applies validated suppression overlays to matching findings without deleting the underlying finding records.
        /// </summary>
        /// <param name="suppressionRequests">The suppression requests to persist and apply.</param>
        /// <param name="cancellationToken">The cancellation token that can stop suppression before adapter work starts.</param>
        /// <returns>A result describing the suppression outcome and validation diagnostics.</returns>
        Task<SuppressionPersistenceResult> SuppressFindingsAsync(IEnumerable<SuppressFindingRequest> suppressionRequests, CancellationToken cancellationToken);
    }
}
