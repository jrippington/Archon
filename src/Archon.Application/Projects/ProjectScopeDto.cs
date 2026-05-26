namespace Archon.Application.Projects
{
    /// <summary>
    /// Describes the repository and optional solution scope applied to a project query.
    /// </summary>
    /// <param name="RepositoryStableKey">The repository stable key that bounded snapshot resolution.</param>
    /// <param name="RepositoryName">The repository display name when available.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrowed the project scope.</param>
    /// <param name="SolutionName">The optional solution display name when available.</param>
    public sealed record ProjectScopeDto(string RepositoryStableKey, string? RepositoryName, string? SolutionStableKey, string? SolutionName)
    {
        // Scope values are stable public identifiers and display names only; persistence-local identifiers are intentionally absent.
    }
}
