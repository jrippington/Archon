namespace ArchonMcp.McpRules
{
    /// <summary>
    /// Represents one safe architecture-rule catalog record returned by MCP.
    /// </summary>
    /// <param name="RuleCode">The stable rule code.</param>
    /// <param name="Version">The exact rule version.</param>
    /// <param name="Name">The human-readable rule name.</param>
    /// <param name="Category">The rule category.</param>
    /// <param name="Severity">The default severity assigned to findings produced by the rule.</param>
    /// <param name="DefaultStatus">The default finding status authored by the rule.</param>
    /// <param name="Enabled">A value indicating whether the rule is enabled for evaluation.</param>
    /// <param name="BuiltIn">A value indicating whether Archon ships the rule as built-in catalog content.</param>
    /// <param name="OwnerScope">The optional owner scope for organization-specific rules.</param>
    /// <param name="Description">The safe description or summary for the rule.</param>
    /// <param name="ApplicableScopes">The deterministic tag and scope labels associated with the rule.</param>
    /// <param name="RelatedFindingCount">The related finding count when the query layer supplies one; otherwise <see langword="null" />.</param>
    /// <param name="SourceReferences">Safe source references for the rule definition when available.</param>
    public sealed record ArchonMcpArchitectureRuleRecord(
        string RuleCode,
        string Version,
        string Name,
        string Category,
        string Severity,
        string DefaultStatus,
        bool Enabled,
        bool BuiltIn,
        string? OwnerScope,
        string Description,
        IReadOnlyList<string> ApplicableScopes,
        int? RelatedFindingCount,
        IReadOnlyList<string> SourceReferences);
}
