namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the current API-visible state of an extraction run.
    /// </summary>
    /// <param name="RunId">The stable public run identifier.</param>
    /// <param name="Status">The current lifecycle status name.</param>
    /// <param name="SubmittedRequest">The accepted request summary.</param>
    /// <param name="StartedUtc">The UTC timestamp when the run was accepted.</param>
    /// <param name="CompletedUtc">The optional UTC timestamp when the run reached a terminal state.</param>
    /// <param name="Progress">The current progress details.</param>
    /// <param name="WarningCount">The number of warning diagnostics recorded so far.</param>
    /// <param name="ErrorCount">The number of error diagnostics recorded so far.</param>
    /// <param name="Timings">The measured extraction stage durations recorded so far.</param>
    /// <param name="SnapshotIdentity">The optional persisted snapshot stable identity.</param>
    public sealed record ExtractionRunStatusResponse(
        string RunId,
        string Status,
        ExtractionRunRequestSummaryResponse SubmittedRequest,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        ExtractionRunProgressResponse Progress,
        int WarningCount,
        int ErrorCount,
        IReadOnlyList<ExtractionRunTimingResponse> Timings,
        string? SnapshotIdentity);
}
