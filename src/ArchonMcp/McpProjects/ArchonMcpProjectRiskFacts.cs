namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Carries project-level risk and quality indicators available to the MCP project description response.
    /// </summary>
    /// <param name="HotlistFindingCount">The number of hotlist findings targeting the project.</param>
    /// <param name="HotlistFindingStableKeys">The hotlist finding stable keys associated with the project.</param>
    /// <param name="HasHotlistFindings">A value indicating whether the project is associated with one or more hotlist findings.</param>
    /// <param name="HighestSeverity">The highest known hotlist or finding severity targeting the project when available.</param>
    /// <param name="HasUnknownData">A value indicating whether the project includes explicit unknown-state metadata.</param>
    /// <param name="UnknownReason">The safe reason explaining unknown project data when available.</param>
    /// <param name="Confidence">The normalized project confidence as a decimal value from the query layer.</param>
    public sealed record ArchonMcpProjectRiskFacts(
        int HotlistFindingCount,
        IReadOnlyList<string> HotlistFindingStableKeys,
        bool HasHotlistFindings,
        string? HighestSeverity,
        bool HasUnknownData,
        string? UnknownReason,
        decimal Confidence);
}
