namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Carries structured facts returned by the current hotspots MCP resource.
    /// </summary>
    /// <param name="ResourceUri">The canonical resource URI that produced these facts.</param>
    /// <param name="SnapshotStableKey">The selected current snapshot stable key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that scoped current selection.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that scoped current selection.</param>
    /// <param name="CategoryFilter">The optional hotspot category filter applied to the resource.</param>
    /// <param name="TotalMatchingHotspots">The total number of matching hotspots before MCP limiting.</param>
    /// <param name="Hotspots">The bounded hotspot records returned by the resource.</param>
    public sealed record ArchonMcpHotspotsCurrentResourceFacts(
        string ResourceUri,
        string SnapshotStableKey,
        string RepositoryStableKey,
        string? SolutionStableKey,
        string? CategoryFilter,
        int TotalMatchingHotspots,
        IReadOnlyList<ArchonMcpHotspotRecord> Hotspots);
}
