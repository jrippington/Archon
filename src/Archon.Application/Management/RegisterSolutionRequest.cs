namespace Archon.Application.Management
{
    /// <summary>
    /// Captures solution registration input associated with a registered repository.
    /// </summary>
    /// <param name="RepositoryStableKey">The stable key of the repository containing the solution.</param>
    /// <param name="SolutionStableKey">The caller-supplied stable solution identity.</param>
    /// <param name="Name">The developer-facing solution name.</param>
    /// <param name="Path">The repository-relative solution path to register.</param>
    /// <param name="Metadata">The optional approved metadata fields supplied by the caller.</param>
    /// <param name="RequestedBy">The optional actor identity used for audit metadata.</param>
    public sealed record RegisterSolutionRequest(
        string? RepositoryStableKey,
        string? SolutionStableKey,
        string? Name,
        string? Path,
        IReadOnlyDictionary<string, string>? Metadata,
        string? RequestedBy);
}
