namespace Archon.Application.Diff
{
    /// <summary>
    /// Defines controlled application behavior for comparing two persisted architecture snapshots.
    /// </summary>
    public interface ISnapshotDiffService
    {
        /// <summary>
        /// Compares two snapshots using stable keys and normalized fingerprints.
        /// </summary>
        /// <param name="query">The controlled snapshot diff request.</param>
        /// <param name="cancellationToken">The token that can cancel comparison before snapshot data is read.</param>
        /// <returns>A snapshot diff result containing summaries, bounded details, truncation metadata, or validation errors.</returns>
        Task<SnapshotDiffResult> CompareSnapshotsAsync(SnapshotDiffQuery query, CancellationToken cancellationToken);
    }
}
