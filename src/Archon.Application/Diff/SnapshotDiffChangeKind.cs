namespace Archon.Application.Diff
{
    /// <summary>
    /// Defines controlled change-kind values returned by snapshot diff comparisons.
    /// </summary>
    public static class SnapshotDiffChangeKind
    {
        /// <summary>
        /// Identifies records that exist only in the current snapshot.
        /// </summary>
        public const string Added = "Added";

        /// <summary>
        /// Identifies records that exist only in the previous snapshot.
        /// </summary>
        public const string Removed = "Removed";

        /// <summary>
        /// Identifies records that share a stable key but have different normalized fingerprints.
        /// </summary>
        public const string Changed = "Changed";

        /// <summary>
        /// Identifies records that share a stable key and normalized fingerprint.
        /// </summary>
        public const string Unchanged = "Unchanged";

        /// <summary>
        /// Lists all supported change kinds in deterministic validation order.
        /// </summary>
        public static readonly IReadOnlyList<string> All = [Added, Removed, Changed, Unchanged];
    }
}
