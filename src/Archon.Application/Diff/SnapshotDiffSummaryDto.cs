namespace Archon.Application.Diff
{
    /// <summary>
    /// Summarizes snapshot diff counts for one compared record domain.
    /// </summary>
    /// <param name="Domain">The controlled diff domain, such as Nodes, Edges, Findings, or Metrics.</param>
    /// <param name="AddedCount">The number of records present only in the current snapshot.</param>
    /// <param name="RemovedCount">The number of records present only in the previous snapshot.</param>
    /// <param name="ChangedCount">The number of records with matching stable keys but different fingerprints.</param>
    /// <param name="UnchangedCount">The number of records with matching stable keys and equal fingerprints.</param>
    public sealed record SnapshotDiffSummaryDto(
        string Domain,
        int AddedCount,
        int RemovedCount,
        int ChangedCount,
        int UnchangedCount);
}
