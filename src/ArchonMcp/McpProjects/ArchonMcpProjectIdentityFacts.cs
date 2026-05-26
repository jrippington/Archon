namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Describes stable project identity facts returned by the <c>archon.describe_project</c> MCP tool.
    /// </summary>
    /// <param name="StableKey">The durable public project stable key.</param>
    /// <param name="Name">The project display name.</param>
    /// <param name="Path">The repository-relative project path when available.</param>
    /// <param name="Language">The programming or artifact language associated with the project when known.</param>
    /// <param name="TargetFramework">The target framework value when extraction provided one.</param>
    /// <param name="ProjectFormat">The known project file format, such as SDK-style or non-SDK-style, when available.</param>
    /// <param name="ApplicationType">The application type classification when available.</param>
    /// <param name="ProjectType">The broader project type or architecture layer classification when available.</param>
    public sealed record ArchonMcpProjectIdentityFacts(
        string StableKey,
        string Name,
        string? Path,
        string? Language,
        string? TargetFramework,
        string? ProjectFormat,
        string? ApplicationType,
        string? ProjectType);
}
