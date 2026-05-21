namespace Archon.Application.Extraction.Requests
{
    /// <summary>
    /// Represents the application-layer command values required to start an architecture extraction run.
    /// </summary>
    /// <remarks>
    /// The command is independent of HTTP and can be created by API endpoints, tests, or future worker entry points. It preserves the submitted repository and solution values while validation produces the normalized execution form.
    /// </remarks>
    /// <param name="RepositoryRootDirectory">The repository root directory submitted by the caller.</param>
    /// <param name="SolutionPaths">The explicit solution paths submitted by the caller.</param>
    /// <param name="BranchName">The optional source-control branch name associated with the request.</param>
    /// <param name="CommitSha">The optional source-control commit SHA associated with the request.</param>
    /// <param name="RequestedBy">The optional actor or system that requested extraction.</param>
    /// <param name="Metadata">The optional deterministic metadata values supplied by the caller.</param>
    public sealed record StartExtractionRequest(
        string? RepositoryRootDirectory,
        IReadOnlyList<string>? SolutionPaths,
        string? BranchName,
        string? CommitSha,
        string? RequestedBy,
        IReadOnlyDictionary<string, string>? Metadata);
}
