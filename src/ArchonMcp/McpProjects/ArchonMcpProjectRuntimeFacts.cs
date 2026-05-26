namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Groups project-owned runtime and integration facts returned by the project description tool.
    /// </summary>
    /// <param name="EntryPoints">The stable entry-point names or keys associated with the project.</param>
    /// <param name="Endpoints">The endpoint names or stable keys owned by the project.</param>
    /// <param name="Workers">The worker or hosted-service names owned by the project.</param>
    /// <param name="DataAccess">The data-access indicators, nodes, or relationships owned by the project.</param>
    /// <param name="ConfigurationKeys">The configuration keys used by the project.</param>
    /// <param name="Integrations">The integration names, stable keys, or external service references associated with the project.</param>
    public sealed record ArchonMcpProjectRuntimeFacts(
        IReadOnlyList<string> EntryPoints,
        IReadOnlyList<string> Endpoints,
        IReadOnlyList<string> Workers,
        IReadOnlyList<string> DataAccess,
        IReadOnlyList<string> ConfigurationKeys,
        IReadOnlyList<string> Integrations);
}
