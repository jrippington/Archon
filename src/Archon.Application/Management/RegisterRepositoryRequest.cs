namespace Archon.Application.Management
{
    /// <summary>
    /// Captures repository registration input accepted by the controlled management surface.
    /// </summary>
    /// <param name="RepositoryStableKey">The caller-supplied stable repository identity.</param>
    /// <param name="Name">The developer-facing repository name.</param>
    /// <param name="RootPath">The repository root path metadata to register without starting extraction.</param>
    /// <param name="RemoteUrl">The optional source-control remote URL metadata.</param>
    /// <param name="DefaultBranch">The optional default branch metadata.</param>
    /// <param name="Metadata">The optional approved metadata fields supplied by the caller.</param>
    /// <param name="RequestedBy">The optional actor identity used for audit metadata.</param>
    public sealed record RegisterRepositoryRequest(
        string? RepositoryStableKey,
        string? Name,
        string? RootPath,
        string? RemoteUrl,
        string? DefaultBranch,
        IReadOnlyDictionary<string, string>? Metadata,
        string? RequestedBy);
}
