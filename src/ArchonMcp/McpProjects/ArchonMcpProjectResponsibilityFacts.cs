namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Describes one evidence-backed or deterministically inferred project responsibility in the MCP project facts section.
    /// </summary>
    /// <param name="Name">The stable responsibility name.</param>
    /// <param name="Description">The developer-facing responsibility explanation supplied by the query layer.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys supporting the responsibility.</param>
    public sealed record ArchonMcpProjectResponsibilityFacts(
        string Name,
        string Description,
        IReadOnlyList<string> EvidenceStableKeys);
}
