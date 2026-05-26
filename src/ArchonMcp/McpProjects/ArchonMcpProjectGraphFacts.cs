namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Summarizes project-scoped graph counts and relationship keys returned by the project description tool.
    /// </summary>
    /// <param name="OutgoingDependencyCount">The number of outgoing dependency or reference relationships from the project.</param>
    /// <param name="IncomingDependentCount">The number of incoming dependency or reference relationships to the project.</param>
    /// <param name="PackageCount">The number of package relationships associated with the project.</param>
    /// <param name="EndpointCount">The number of endpoint facts owned by the project.</param>
    /// <param name="NodeCount">The number of project-owned or directly related nodes in the scoped graph summary.</param>
    /// <param name="DataAccessCount">The number of data-access facts in the scoped graph summary.</param>
    /// <param name="IntegrationCount">The number of integration facts in the scoped graph summary.</param>
    /// <param name="Dependencies">The stable keys of projects referenced by the selected project.</param>
    /// <param name="Dependents">The stable keys of projects that reference the selected project.</param>
    /// <param name="Packages">The package names or stable keys associated with the selected project.</param>
    public sealed record ArchonMcpProjectGraphFacts(
        int OutgoingDependencyCount,
        int IncomingDependentCount,
        int PackageCount,
        int EndpointCount,
        int NodeCount,
        int DataAccessCount,
        int IntegrationCount,
        IReadOnlyList<string> Dependencies,
        IReadOnlyList<string> Dependents,
        IReadOnlyList<string> Packages);
}
