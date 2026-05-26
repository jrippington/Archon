namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Represents a safe stable-key project candidate returned when project-name lookup is ambiguous.
    /// </summary>
    /// <param name="StableKey">The durable public project stable key that can disambiguate a later request.</param>
    /// <param name="Name">The project display name that matched the ambiguous lookup.</param>
    /// <param name="Path">The repository-relative project path when available.</param>
    /// <param name="Language">The project language when known.</param>
    /// <param name="TargetFramework">The target framework when known.</param>
    public sealed record ArchonMcpProjectDisambiguationCandidate(
        string StableKey,
        string Name,
        string? Path,
        string? Language,
        string? TargetFramework);
}
