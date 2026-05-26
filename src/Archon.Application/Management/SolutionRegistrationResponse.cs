namespace Archon.Application.Management
{
    /// <summary>
    /// Represents a registered solution path under a repository scope.
    /// </summary>
    /// <param name="RepositoryStableKey">The stable key of the repository containing the solution.</param>
    /// <param name="SolutionStableKey">The stable solution identity accepted by management registration.</param>
    /// <param name="Name">The developer-facing solution name.</param>
    /// <param name="Path">The normalized repository-relative solution path.</param>
    /// <param name="Metadata">The approved metadata fields retained for the solution.</param>
    /// <param name="Audit">The audit-ready metadata for the accepted registration action.</param>
    public sealed record SolutionRegistrationResponse(
        string RepositoryStableKey,
        string SolutionStableKey,
        string Name,
        string Path,
        IReadOnlyDictionary<string, string> Metadata,
        AuditMetadataResponse Audit);
}
