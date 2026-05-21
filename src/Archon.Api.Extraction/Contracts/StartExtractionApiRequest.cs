namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the JSON request body accepted by POST /extractions.
    /// </summary>
    /// <param name="RepositoryRootDirectory">The repository root directory submitted by the API consumer.</param>
    /// <param name="SolutionPaths">The explicit solution paths submitted by the API consumer.</param>
    /// <param name="BranchName">The optional source-control branch name associated with the extraction request.</param>
    /// <param name="CommitSha">The optional source-control commit SHA associated with the extraction request.</param>
    /// <param name="RequestedBy">The optional actor or system that requested extraction.</param>
    /// <param name="Metadata">The optional deterministic metadata values supplied by the API consumer.</param>
    public sealed record StartExtractionApiRequest(
        string? RepositoryRootDirectory,
        IReadOnlyList<string>? SolutionPaths,
        string? BranchName,
        string? CommitSha,
        string? RequestedBy,
        IReadOnlyDictionary<string, string>? Metadata);
}
