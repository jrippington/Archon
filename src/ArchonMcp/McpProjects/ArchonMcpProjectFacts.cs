namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Represents the structured facts section returned by the <c>archon.describe_project</c> MCP tool.
    /// </summary>
    /// <param name="Identity">The stable identity, path, language, target framework, format, and application-type facts for the selected project.</param>
    /// <param name="Graph">The dependency, dependent, package, endpoint, data-access, and integration graph summary facts.</param>
    /// <param name="Runtime">The endpoint, worker, entry-point, data-access, configuration, and integration lists associated with the project.</param>
    /// <param name="Responsibilities">The persisted or deterministically derived responsibilities associated with the project.</param>
    /// <param name="Risk">The hotlist, unknown-state, and confidence indicators for the project.</param>
    /// <param name="Metadata">The sanitized supplemental project metadata exposed by the query layer.</param>
    public sealed record ArchonMcpProjectFacts(
        ArchonMcpProjectIdentityFacts Identity,
        ArchonMcpProjectGraphFacts Graph,
        ArchonMcpProjectRuntimeFacts Runtime,
        IReadOnlyList<ArchonMcpProjectResponsibilityFacts> Responsibilities,
        ArchonMcpProjectRiskFacts Risk,
        IReadOnlyDictionary<string, string> Metadata);
}
