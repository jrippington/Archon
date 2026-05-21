namespace Archon.Application.Extraction.Resolution
{
    /// <summary>
    /// Represents the normalized and validated input that later extraction orchestration can safely execute.
    /// </summary>
    /// <param name="RepositoryRootDirectory">The normalized absolute repository root directory.</param>
    /// <param name="SolutionPaths">The normalized absolute solution file paths inside the repository root.</param>
    /// <param name="BranchName">The optional source-control branch name preserved from the start request.</param>
    /// <param name="CommitSha">The optional source-control commit SHA preserved from the start request.</param>
    /// <param name="RequestedBy">The optional actor or system preserved from the start request.</param>
    /// <param name="Metadata">The deterministic metadata values preserved from the start request.</param>
    public sealed record ResolvedExtractionInput(
        string RepositoryRootDirectory,
        IReadOnlyList<string> SolutionPaths,
        string? BranchName,
        string? CommitSha,
        string? RequestedBy,
        IReadOnlyDictionary<string, string> Metadata);
}
