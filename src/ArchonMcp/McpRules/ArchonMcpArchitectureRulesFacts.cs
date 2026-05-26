namespace ArchonMcp.McpRules
{
    /// <summary>
    /// Carries the structured facts returned by the architecture-rule catalog MCP tool.
    /// </summary>
    /// <param name="RuleCodeFilter">The exact rule-code filter applied to the query when supplied.</param>
    /// <param name="CategoryFilter">The exact category filter applied to the query when supplied.</param>
    /// <param name="SeverityFilter">The exact severity filter applied to the query when supplied.</param>
    /// <param name="EnabledFilter">The enabled-state filter applied to the query when supplied.</param>
    /// <param name="TotalMatchingRules">The total number of matching rules before MCP response limiting.</param>
    /// <param name="Rules">The bounded deterministic rule catalog records returned to the caller.</param>
    public sealed record ArchonMcpArchitectureRulesFacts(
        string? RuleCodeFilter,
        string? CategoryFilter,
        string? SeverityFilter,
        bool? EnabledFilter,
        int TotalMatchingRules,
        IReadOnlyList<ArchonMcpArchitectureRuleRecord> Rules);
}
