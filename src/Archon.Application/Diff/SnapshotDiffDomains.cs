namespace Archon.Application.Diff
{
    /// <summary>
    /// Defines controlled record domains that can participate in a snapshot diff comparison.
    /// </summary>
    public static class SnapshotDiffDomains
    {
        /// <summary>
        /// Identifies architecture node records in a snapshot diff.
        /// </summary>
        public const string Nodes = "Nodes";

        /// <summary>
        /// Identifies architecture edge records in a snapshot diff.
        /// </summary>
        public const string Edges = "Edges";

        /// <summary>
        /// Identifies finding records in a snapshot diff.
        /// </summary>
        public const string Findings = "Findings";

        /// <summary>
        /// Identifies metric records in a snapshot diff.
        /// </summary>
        public const string Metrics = "Metrics";

        /// <summary>
        /// Lists all supported diff domains in deterministic comparison order.
        /// </summary>
        public static readonly IReadOnlyList<string> All = [Nodes, Edges, Findings, Metrics];
    }
}
