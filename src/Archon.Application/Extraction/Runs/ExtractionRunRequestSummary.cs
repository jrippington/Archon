namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Captures the credential-safe submitted request values retained in operational run history.
    /// </summary>
    /// <param name="RepositoryRootDirectory">The normalized repository root directory accepted for extraction.</param>
    /// <param name="SolutionPaths">The normalized solution paths accepted for extraction.</param>
    /// <param name="BranchName">The optional source-control branch name supplied by the caller.</param>
    /// <param name="CommitSha">The optional source-control commit SHA supplied by the caller.</param>
    /// <param name="RequestedBy">The optional actor or system that requested extraction.</param>
    /// <param name="MetadataKeys">The sorted metadata keys retained without sensitive metadata values.</param>
    public sealed record ExtractionRunRequestSummary(
        string RepositoryRootDirectory,
        IReadOnlyList<string> SolutionPaths,
        string? BranchName,
        string? CommitSha,
        string? RequestedBy,
        IReadOnlyList<string> MetadataKeys);
}
