namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Represents the result of resolving a current snapshot for MCP resources.
    /// </summary>
    public sealed class ArchonMcpCurrentSnapshotResolution
    {
        /// <summary>
        /// Initializes a current snapshot resolution outcome.
        /// </summary>
        /// <param name="kind">The resolution outcome kind.</param>
        /// <param name="snapshot">The selected snapshot when resolution succeeded.</param>
        /// <param name="candidateSnapshotStableKeys">The candidate keys when resolution was ambiguous.</param>
        /// <param name="message">The safe explanation for non-success outcomes.</param>
        private ArchonMcpCurrentSnapshotResolution(
            ArchonMcpCurrentSnapshotResolutionKind kind,
            ArchonMcpCurrentSnapshotContext? snapshot,
            IReadOnlyList<string> candidateSnapshotStableKeys,
            string? message)
        {
            // The resolution stores only stable keys and counts so MCP never needs to expose persistence-local identifiers.
            Kind = kind;
            Snapshot = snapshot;
            CandidateSnapshotStableKeys = candidateSnapshotStableKeys;
            Message = message;
        }

        /// <summary>
        /// Gets the resolution outcome kind.
        /// </summary>
        public ArchonMcpCurrentSnapshotResolutionKind Kind { get; }

        /// <summary>
        /// Gets the selected snapshot when resolution succeeded.
        /// </summary>
        public ArchonMcpCurrentSnapshotContext? Snapshot { get; }

        /// <summary>
        /// Gets candidate snapshot stable keys when resolution was ambiguous.
        /// </summary>
        public IReadOnlyList<string> CandidateSnapshotStableKeys { get; }

        /// <summary>
        /// Gets a safe explanation for non-success outcomes.
        /// </summary>
        public string? Message { get; }

        /// <summary>
        /// Creates a successful current snapshot resolution.
        /// </summary>
        /// <param name="snapshot">The selected current snapshot context.</param>
        /// <returns>A successful resolution.</returns>
        public static ArchonMcpCurrentSnapshotResolution Success(ArchonMcpCurrentSnapshotContext snapshot)
        {
            // Success carries the selected snapshot and no ambiguity candidates.
            return new ArchonMcpCurrentSnapshotResolution(ArchonMcpCurrentSnapshotResolutionKind.Success, snapshot, [], null);
        }

        /// <summary>
        /// Creates a not-found current snapshot resolution.
        /// </summary>
        /// <param name="message">The safe not-found explanation.</param>
        /// <returns>A not-found resolution.</returns>
        public static ArchonMcpCurrentSnapshotResolution NotFound(string message)
        {
            // Not-found responses do not reveal any persistence adapter or query store details.
            return new ArchonMcpCurrentSnapshotResolution(ArchonMcpCurrentSnapshotResolutionKind.NotFound, null, [], message);
        }

        /// <summary>
        /// Creates an ambiguous current snapshot resolution.
        /// </summary>
        /// <param name="candidateSnapshotStableKeys">The stable keys of snapshots that tied for current selection.</param>
        /// <param name="message">The safe ambiguity explanation.</param>
        /// <returns>An ambiguous resolution.</returns>
        public static ArchonMcpCurrentSnapshotResolution Ambiguous(IReadOnlyList<string> candidateSnapshotStableKeys, string message)
        {
            // Ambiguity reports only stable keys so clients can ask for explicit selection in later slices without seeing internal IDs.
            return new ArchonMcpCurrentSnapshotResolution(ArchonMcpCurrentSnapshotResolutionKind.Ambiguous, null, candidateSnapshotStableKeys, message);
        }
    }
}
