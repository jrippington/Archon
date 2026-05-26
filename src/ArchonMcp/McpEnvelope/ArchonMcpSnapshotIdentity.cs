namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Identifies the snapshot state used by an MCP response when the operation depends on persisted graph data.
    /// </summary>
    public sealed record ArchonMcpSnapshotIdentity
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpSnapshotIdentity" /> record.
        /// </summary>
        /// <param name="stableKey">The stable snapshot key returned by the query layer, such as a <c>snapshot://</c> identity.</param>
        /// <param name="selectionMode">The selection mode used by the request, such as <c>latest</c> or <c>explicit</c>.</param>
        /// <param name="description">A safe description of how the snapshot was resolved.</param>
        public ArchonMcpSnapshotIdentity(string stableKey, string selectionMode, string description)
        {
            // Snapshot identity remains explicit so AI clients do not confuse data from different extraction states.
            StableKey = stableKey;
            SelectionMode = selectionMode;
            Description = description;
        }

        /// <summary>
        /// Gets the stable snapshot key returned by the query layer.
        /// </summary>
        public string StableKey { get; init; }

        /// <summary>
        /// Gets the selection mode used to resolve the snapshot.
        /// </summary>
        public string SelectionMode { get; init; }

        /// <summary>
        /// Gets the safe description of how the snapshot was resolved.
        /// </summary>
        public string Description { get; init; }
    }
}
