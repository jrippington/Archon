namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Represents one safe, stable-key-based <c>archon.search</c> result item.
    /// </summary>
    public sealed record ArchonMcpSearchResultItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpSearchResultItem" /> record.
        /// </summary>
        /// <param name="stableKey">The stable public identity of the matched architecture record.</param>
        /// <param name="entityKind">The controlled entity kind for the matched record.</param>
        /// <param name="displayText">The concise display text returned by the query layer.</param>
        /// <param name="summary">The safe summary explaining why the result matched.</param>
        /// <param name="snapshotStableKey">The snapshot stable key associated with the matched record.</param>
        /// <param name="evidenceStableKeys">Stable evidence identities that support the result where available.</param>
        /// <param name="relatedStableKeys">Stable related-node identities that help a caller continue investigation.</param>
        /// <param name="hasUnknownData">Indicates whether the row carries unknown-state information.</param>
        /// <param name="unknownReason">The safe unknown-state reason for the row when present.</param>
        /// <param name="confidence">The confidence assigned to this result item.</param>
        /// <param name="suggestedFollowUps">Safe follow-up operations or routes for investigating this result.</param>
        public ArchonMcpSearchResultItem(
            string stableKey,
            string entityKind,
            string displayText,
            string summary,
            string snapshotStableKey,
            IEnumerable<string>? evidenceStableKeys,
            IEnumerable<string>? relatedStableKeys,
            bool hasUnknownData,
            string? unknownReason,
            string confidence,
            IEnumerable<ArchonMcpSearchSuggestedFollowUp>? suggestedFollowUps)
        {
            // The item snapshots collection values so the MCP envelope remains deterministic after query mapping finishes.
            StableKey = stableKey;
            EntityKind = entityKind;
            DisplayText = displayText;
            Summary = summary;
            SnapshotStableKey = snapshotStableKey;
            EvidenceStableKeys = evidenceStableKeys?.ToArray() ?? [];
            RelatedStableKeys = relatedStableKeys?.ToArray() ?? [];
            HasUnknownData = hasUnknownData;
            UnknownReason = unknownReason;
            Confidence = confidence;
            SuggestedFollowUps = suggestedFollowUps?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the stable public identity of the matched architecture record.
        /// </summary>
        public string StableKey { get; init; }

        /// <summary>
        /// Gets the controlled entity kind for the matched record.
        /// </summary>
        public string EntityKind { get; init; }

        /// <summary>
        /// Gets the concise display text returned by the query layer.
        /// </summary>
        public string DisplayText { get; init; }

        /// <summary>
        /// Gets the safe summary explaining why the result matched.
        /// </summary>
        public string Summary { get; init; }

        /// <summary>
        /// Gets the snapshot stable key associated with the matched record.
        /// </summary>
        public string SnapshotStableKey { get; init; }

        /// <summary>
        /// Gets stable evidence identities that support the result where available.
        /// </summary>
        public IReadOnlyList<string> EvidenceStableKeys { get; init; }

        /// <summary>
        /// Gets stable related-node identities that help a caller continue investigation.
        /// </summary>
        public IReadOnlyList<string> RelatedStableKeys { get; init; }

        /// <summary>
        /// Gets a value indicating whether the row carries unknown-state information.
        /// </summary>
        public bool HasUnknownData { get; init; }

        /// <summary>
        /// Gets the safe unknown-state reason for the row when present.
        /// </summary>
        public string? UnknownReason { get; init; }

        /// <summary>
        /// Gets the confidence assigned to this result item as a simple MCP-friendly value.
        /// </summary>
        public string Confidence { get; init; }

        /// <summary>
        /// Gets safe follow-up operations or routes for investigating this result.
        /// </summary>
        public IReadOnlyList<ArchonMcpSearchSuggestedFollowUp> SuggestedFollowUps { get; init; }
    }
}
