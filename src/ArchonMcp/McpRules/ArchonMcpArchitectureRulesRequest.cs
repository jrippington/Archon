namespace ArchonMcp.McpRules
{
    /// <summary>
    /// Describes read-only filters for the <c>archon.get_architecture_rules</c> MCP tool.
    /// </summary>
    /// <param name="RuleCode">The optional exact architecture rule code filter.</param>
    /// <param name="Category">The optional exact architecture rule category filter.</param>
    /// <param name="Severity">The optional default severity filter.</param>
    /// <param name="Enabled">The optional enabled-state filter for the rule catalog.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector retained for MCP consistency; catalog queries are not snapshot-mutating.</param>
    /// <param name="Limit">The optional maximum number of rule catalog records returned by MCP.</param>
    public sealed record ArchonMcpArchitectureRulesRequest(
        string? RuleCode,
        string? Category,
        string? Severity,
        bool? Enabled,
        string? SnapshotSelector,
        int? Limit);
}
