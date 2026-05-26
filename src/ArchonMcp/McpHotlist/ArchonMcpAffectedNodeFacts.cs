namespace ArchonMcp.McpHotlist
{
    /// <summary>
    /// Represents a safe affected-node reference for a hotlist finding.
    /// </summary>
    /// <param name="StableKey">The stable architecture node key.</param>
    /// <param name="DisplayName">The safe display name for the node.</param>
    /// <param name="NodeKind">The optional architecture node kind.</param>
    /// <param name="ProjectStableKey">The optional project stable key associated with the affected node.</param>
    public sealed record ArchonMcpAffectedNodeFacts(
        string StableKey,
        string DisplayName,
        string? NodeKind,
        string? ProjectStableKey);
}
