namespace Archon.Application.Management
{
    /// <summary>
    /// Represents a registered repository without exposing arbitrary persistence internals.
    /// </summary>
    /// <param name="RepositoryStableKey">The stable repository identity accepted by management registration.</param>
    /// <param name="Name">The developer-facing repository name.</param>
    /// <param name="RootPath">The registered repository root metadata.</param>
    /// <param name="RemoteUrl">The optional source-control remote URL metadata.</param>
    /// <param name="DefaultBranch">The optional default branch metadata.</param>
    /// <param name="Metadata">The approved metadata fields retained for the repository.</param>
    /// <param name="Audit">The audit-ready metadata for the accepted registration action.</param>
    public sealed record RepositoryRegistrationResponse(
        string RepositoryStableKey,
        string Name,
        string RootPath,
        string? RemoteUrl,
        string? DefaultBranch,
        IReadOnlyDictionary<string, string> Metadata,
        AuditMetadataResponse Audit);
}
