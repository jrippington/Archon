using ArchonMcp.McpRuntime;

namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Implements the safe baseline operation that proves common MCP envelope shaping before user-facing tools are mapped.
    /// </summary>
    internal sealed class ArchonMcpBaselineOperation : IArchonMcpBaselineOperation
    {
        /// <summary>
        /// Builds the read-only baseline health envelope without invoking application mutation or external execution behavior.
        /// </summary>
        /// <returns>A common MCP envelope describing the operational baseline capability.</returns>
        public ArchonMcpEnvelope<IReadOnlyList<ArchonMcpFact>> GetHealthEnvelope()
        {
            // The baseline fact is intentionally operational and static; it proves envelope composition without querying Neo4j or files.
            ArchonMcpConfidence confidence = new(
                ArchonMcpConfidenceLevel.High,
                "The host composed the mandatory read-only baseline capability and common MCP envelope services.");
            ArchonMcpFact fact = new(
                "mcp-runtime-baseline",
                "OperationalCapability",
                "Read-only MCP runtime baseline",
                "The Archon MCP host has composed the read-only baseline registration catalog and envelope contracts.",
                confidence,
                new Dictionary<string, string>
                {
                    ["capabilityName"] = ArchonMcpBaselineCapabilities.Health.Name,
                    ["readOnly"] = "true"
                });

            return new ArchonMcpEnvelope<IReadOnlyList<ArchonMcpFact>>(
                ArchonMcpBaselineCapabilities.Health.Name,
                snapshot: null,
                "Archon MCP runtime baseline is ready.",
                confidence,
                [fact],
                evidence: null,
                findings: null,
                unknowns: null,
                warnings: null,
                ArchonMcpLimitMetadata.None("resultCount", 1),
                [new ArchonMcpSuggestedFollowUp("Check MCP runtime readiness.", "archon.health", null)]);
        }
    }
}
