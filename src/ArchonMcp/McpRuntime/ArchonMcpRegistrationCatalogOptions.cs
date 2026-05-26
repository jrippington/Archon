namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Configures the mandatory capability names that must be present before the Archon MCP host reports readiness.
    /// </summary>
    /// <remarks>
    /// Work Item 1 requires readiness to fail closed when mandatory registrations are incomplete. The defaults include the
    /// operational baseline capability only; later WP015 slices can extend this list when tools, resources, and prompts become
    /// required runtime capabilities.
    /// </remarks>
    public sealed class ArchonMcpRegistrationCatalogOptions
    {
        /// <summary>
        /// Gets the configuration section name used to bind registration-catalog options.
        /// </summary>
        public const string SectionName = "Archon:Mcp:RegistrationCatalog";

        /// <summary>
        /// Gets or sets the stable capability names that must be registered for the host to be considered ready.
        /// </summary>
        public string[] MandatoryCapabilityNames { get; set; } =
        [
            ArchonMcpBaselineCapabilities.Health.Name,
            ArchonMcpBaselineCapabilities.Search.Name,
            ArchonMcpBaselineCapabilities.DescribeProject.Name,
            ArchonMcpBaselineCapabilities.GetDependencies.Name,
            ArchonMcpBaselineCapabilities.GetDependents.Name,
            ArchonMcpBaselineCapabilities.FindDependencyPaths.Name,
            ArchonMcpBaselineCapabilities.DescribeSymbol.Name,
            ArchonMcpBaselineCapabilities.FindSymbolUsages.Name,
            ArchonMcpBaselineCapabilities.GetDataAccessUsage.Name,
            ArchonMcpBaselineCapabilities.AssessChangeImpact.Name,
            ArchonMcpBaselineCapabilities.GetArchitectureRules.Name,
            ArchonMcpBaselineCapabilities.GetHotlistFindings.Name,
            ArchonMcpBaselineCapabilities.GetSnapshotDiff.Name,
            ArchonMcpBaselineCapabilities.ReadResource.Name,
            ArchonMcpBaselineCapabilities.GetPrompt.Name,
            ArchonMcpBaselineCapabilities.ListPrompts.Name,
            ArchonMcpBaselineCapabilities.ImpactAnalysisPrompt.Name,
            ArchonMcpBaselineCapabilities.ModernizationBriefPrompt.Name,
            ArchonMcpBaselineCapabilities.RefactoringPreflightPrompt.Name,
            ArchonMcpBaselineCapabilities.NewFeaturePlacementPrompt.Name,
            ArchonMcpBaselineCapabilities.LegacyDataAccessReviewPrompt.Name,
            ArchonMcpBaselineCapabilities.HotlistSummaryPrompt.Name,
            ArchonMcpBaselineCapabilities.ArchitectureRuleCheckPrompt.Name
        ];
    }
}
