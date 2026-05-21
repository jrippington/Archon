namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the compact API-visible summary of an extraction run returned by the history endpoint.
    /// </summary>
    /// <param name="RunId">The stable public run identifier that callers can use with the status endpoint.</param>
    /// <param name="Status">The current lifecycle status name.</param>
    /// <param name="StartedUtc">The UTC timestamp when the run was accepted.</param>
    /// <param name="CompletedUtc">The optional UTC timestamp when the run reached a terminal state.</param>
    /// <param name="RepositoryRootDirectory">The normalized repository root directory retained in the submitted request summary.</param>
    /// <param name="SolutionCount">The number of submitted solutions accepted for the run.</param>
    /// <param name="WarningCount">The number of warning diagnostics currently recorded for the run.</param>
    /// <param name="ErrorCount">The number of error diagnostics currently recorded for the run.</param>
    /// <param name="SnapshotIdentity">The optional persisted snapshot stable identity when persistence has completed.</param>
    public sealed record ExtractionRunSummaryResponse(
        string RunId,
        string Status,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        string RepositoryRootDirectory,
        int SolutionCount,
        int WarningCount,
        int ErrorCount,
        string? SnapshotIdentity);
}
