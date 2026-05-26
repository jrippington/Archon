namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Provides the safe baseline capability registrations required by the first WP015 MCP runtime slice.
    /// </summary>
    /// <remarks>
    /// The baseline contains only an operational health/capabilities registration. It proves that the MCP host can compose a
    /// catalog and validate readiness without exposing mutation, shell, SQL, Cypher, filesystem, direct Neo4j, or code-editing
    /// capability names.
    /// </remarks>
    public static class ArchonMcpBaselineCapabilities
    {
        /// <summary>
        /// Gets the stable operational capability that represents the read-only MCP host baseline.
        /// </summary>
        public static ArchonMcpCapabilityRegistration Health { get; } = new(
            "archon.health",
            ArchonMcpCapabilityKind.Operational,
            Required: true,
            ReadOnly: true,
            "Reports that the Archon MCP runtime baseline is composed and constrained to read-only behavior.");

        /// <summary>
        /// Gets the stable read-only search tool capability that exposes bounded architecture investigation over query data.
        /// </summary>
        public static ArchonMcpCapabilityRegistration Search { get; } = new(
            "archon.search",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Searches persisted architecture facts through the approved query layer with evidence-backed MCP envelopes.");

        /// <summary>
        /// Gets the stable read-only project description tool capability that exposes project-level persisted architecture facts.
        /// </summary>
        public static ArchonMcpCapabilityRegistration DescribeProject { get; } = new(
            "archon.describe_project",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Describes one project through the approved query layer with dependencies, runtime facts, evidence, findings, metrics, and unknowns.");

        /// <summary>
        /// Gets the stable read-only outgoing dependency traversal tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetDependencies { get; } = new(
            "archon.get_dependencies",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Traverses direct or transitive outgoing dependencies through the approved query layer using stable graph identities.");

        /// <summary>
        /// Gets the stable read-only incoming dependent traversal tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetDependents { get; } = new(
            "archon.get_dependents",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Traverses direct or transitive incoming dependents through the approved query layer using stable graph identities.");

        /// <summary>
        /// Gets the stable read-only dependency-path search tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration FindDependencyPaths { get; } = new(
            "archon.find_dependency_paths",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Finds bounded dependency paths between stable graph identities through the approved query layer.");

        /// <summary>
        /// Gets the stable read-only symbol description tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration DescribeSymbol { get; } = new(
            "archon.describe_symbol",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Describes one symbol through the approved query layer with source context, relationships, evidence, findings, and unknowns.");

        /// <summary>
        /// Gets the stable read-only symbol usage investigation tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration FindSymbolUsages { get; } = new(
            "archon.find_symbol_usages",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Finds bounded callers, references, and usages for one stable symbol through the approved query layer.");

        /// <summary>
        /// Gets the stable read-only data-access usage review tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetDataAccessUsage { get; } = new(
            "archon.get_data_access_usage",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Lists bounded LINQ to SQL, Entity Framework, ADO.NET, raw SQL, stored procedure, typed DataSet, table, entity, and data-context usage facts through the approved query layer.");

        /// <summary>
        /// Gets the stable read-only change-impact assessment tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration AssessChangeImpact { get; } = new(
            "archon.assess_change_impact",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Assesses bounded direct and transitive impacts for supported stable targets through the approved graph traversal query layer.");

        /// <summary>
        /// Gets the stable read-only architecture-rule catalog tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetArchitectureRules { get; } = new(
            "archon.get_architecture_rules",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Lists bounded architecture-rule catalog records through the approved hotlist query layer without exposing rule mutation.");

        /// <summary>
        /// Gets the stable read-only hotlist findings review tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetHotlistFindings { get; } = new(
            "archon.get_hotlist_findings",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Lists bounded hotlist findings, affected nodes, evidence, and unknowns through the approved hotlist query layer.");

        /// <summary>
        /// Gets the stable read-only snapshot diff comparison tool capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetSnapshotDiff { get; } = new(
            "archon.get_snapshot_diff",
            ArchonMcpCapabilityKind.Tool,
            Required: true,
            ReadOnly: true,
            "Compares explicit or latest comparable architecture snapshots through the approved snapshot diff query layer using stable keys and fingerprints.");

        /// <summary>
        /// Gets the stable read-only Archon resource reader capability for supported <c>archon://</c> resources.
        /// </summary>
        public static ArchonMcpCapabilityRegistration ReadResource { get; } = new(
            "archon.read_resource",
            ArchonMcpCapabilityKind.Resource,
            Required: true,
            ReadOnly: true,
            "Reads supported bounded archon:// resources through explicit URI parsing, current snapshot selection, authorization, and approved query seams.");

        /// <summary>
        /// Gets the stable read-only prompt retrieval operation capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration GetPrompt { get; } = new(
            "archon.get_prompt",
            ArchonMcpCapabilityKind.Operational,
            Required: true,
            ReadOnly: true,
            "Retrieves one registered versioned read-only MCP prompt template through the security and audit pipeline.");

        /// <summary>
        /// Gets the stable read-only prompt listing operation capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration ListPrompts { get; } = new(
            "archon.list_prompts",
            ArchonMcpCapabilityKind.Operational,
            Required: true,
            ReadOnly: true,
            "Lists registered versioned read-only MCP prompt templates through the security and audit pipeline.");

        /// <summary>
        /// Gets the stable impact-analysis prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration ImpactAnalysisPrompt { get; } = CreatePrompt("impact-analysis", "Guides evidence-backed change-impact analysis with unknown reporting and safe follow-ups.");

        /// <summary>
        /// Gets the stable modernization-brief prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration ModernizationBriefPrompt { get; } = CreatePrompt("modernization-brief", "Guides evidence-backed modernization brief creation over persisted architecture facts.");

        /// <summary>
        /// Gets the stable refactoring-preflight prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration RefactoringPreflightPrompt { get; } = CreatePrompt("refactoring-preflight", "Guides evidence-backed preflight review before user-planned refactoring work.");

        /// <summary>
        /// Gets the stable new-feature-placement prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration NewFeaturePlacementPrompt { get; } = CreatePrompt("new-feature-placement", "Guides evidence-backed new-feature placement analysis using read-only Archon facts.");

        /// <summary>
        /// Gets the stable legacy-data-access-review prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration LegacyDataAccessReviewPrompt { get; } = CreatePrompt("legacy-data-access-review", "Guides evidence-backed legacy and modern data-access review workflows.");

        /// <summary>
        /// Gets the stable hotlist-summary prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration HotlistSummaryPrompt { get; } = CreatePrompt("hotlist-summary", "Guides evidence-backed summaries of persisted hotlist findings and triage themes.");

        /// <summary>
        /// Gets the stable architecture-rule-check prompt template capability.
        /// </summary>
        public static ArchonMcpCapabilityRegistration ArchitectureRuleCheckPrompt { get; } = CreatePrompt("architecture-rule-check", "Guides evidence-backed rule catalog and related finding checks.");

        /// <summary>
        /// Creates a required read-only prompt template capability registration.
        /// </summary>
        /// <param name="name">The stable prompt template name advertised by the MCP host.</param>
        /// <param name="description">The secret-safe prompt workflow description.</param>
        /// <returns>The prompt capability registration.</returns>
        private static ArchonMcpCapabilityRegistration CreatePrompt(string name, string description)
        {
            // Prompt names intentionally omit the archon prefix because MCP clients retrieve them by their curated workflow names.
            return new ArchonMcpCapabilityRegistration(name, ArchonMcpCapabilityKind.Prompt, Required: true, ReadOnly: true, description);
        }
    }
}
